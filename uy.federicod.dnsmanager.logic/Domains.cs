using CloudFlare.Client.Api.Zones;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Client.Zones;
using CloudFlare.Client.Enumerators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using uy.federicod.dnsmanager.logic.Models;


namespace uy.federicod.dnsmanager.logic
{
    public class Domains
    {
        private readonly Service s;

        public Domains(Service service) { s = service ?? throw new ArgumentNullException(nameof(service)); }

        public List<DomainModel> GetDomains(string AccountId)
        {
            List<DomainModel> results = [];
            string query = "SELECT * FROM Domains WHERE AccountId = @AccountId";

            SqlConnection connection = new(s.DBConnString);
            connection.Open();

            SqlCommand command = new(query, connection);
            command.Parameters.AddWithValue("AccountId", AccountId);

            SqlDataReader reader = command.ExecuteReader();
            while(reader.Read())
            {
                results.Add(new DomainModel()
                {
                    AccountId = AccountId,
                    DelegationType = reader["DelegationType"].ToString(),
                    DomainName = reader["DomainName"].ToString(),
                    ZoneId = reader["ZoneId"].ToString()
                });

            }
            // Agregar NameServers

            return results;

        }

        public async Task<DomainModel?> GetUserDomainAsync(string DomainName, string ZoneId , string AccountId)
        {
            DomainModel result = new();
            string query = "SELECT * FROM Domains WHERE AccountId = @AccountId AND ZoneId = @ZoneId AND DomainName = @DomainName";

            SqlConnection connection = new(s.DBConnString);
            connection.Open();

            SqlCommand command = new(query, connection);
            command.Parameters.AddWithValue("DomainName", DomainName);
            command.Parameters.AddWithValue("ZoneId", ZoneId);
            command.Parameters.AddWithValue("AccountId", AccountId);

            SqlDataReader reader = command.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                result = new DomainModel()
                {
                    AccountId = AccountId,
                    DelegationType = reader["DelegationType"].ToString(),
                    DomainName = reader["DomainName"].ToString(),
                    ZoneId = reader["ZoneId"].ToString()
                };
                count++;
            }

            // Agregar nameservers
            result.NameServers = await GetNameServersAsync(DomainName, ZoneId);


            if (count > 0)
                return result;
            else 
                return null; 

        }

        public Dictionary<string, string> CreateAsync(string DomainName, string ZoneId, 
            string DelegationType, AccountModel accountModel, Service service, 
            [Optional] IPAddress HostIP, [Optional] List<string> NameServers)
        {
            // Obtiene o crea la cuenta de usuario
            AccountModel realAccount = service.GetAccountOrCreate(accountModel.AccountId, accountModel.DisplayName);

            // Crea un modelDomain para usar en las registraciones
            DomainModel model = new()
            {
                DomainName = DomainName,
                ZoneId = ZoneId,
                AccountId = accountModel.AccountId,
                DelegationType = DelegationType
            };
            if (NameServers != null)
            {
                model.NameServers = NameServers;
            }

            Dictionary<string, string> results = [];

            // Registra el dominio en la base datos
            if (AddToDB(model, realAccount))
            {
                // Si pudo agregarlo, lo lleva a Cloudflare 
                if (model.DelegationType == "Hosted")
                {
                    // Agrega un registro A
                    if(RegisterHosted(model, HostIP))
                    {
                        results.Add(model.DomainName, "Ok");
                    }
                    else
                    {
                        results.Add(model.DomainName, "Failed");
                    }
                }
                else
                {
                    // Delega el dominio en una lista de NS
                    results = RegisterDelegated(model);
                }
            }
            else
            {
                throw new Exception("Can not create domain");
            }
            return results;
        }

        private bool AddToDB(DomainModel domainModel, AccountModel accountModel)
        {
            try
            {
                string query = "INSERT INTO dbo.Domains (DomainName, ZoneId, AccountId, DelegationType) VALUES (@DomainName, @ZoneId, @AccountId, @DelegationType)";
                SqlConnection connection = new(s.DBConnString);
                connection.Open();

                SqlCommand command = new(query, connection);
                command.Parameters.AddWithValue("DomainName", domainModel.DomainName);
                command.Parameters.AddWithValue("ZoneId", domainModel.ZoneId);
                command.Parameters.AddWithValue("AccountId", accountModel.AccountId);
                command.Parameters.AddWithValue("DelegationType", domainModel.DelegationType);

                int result = command.ExecuteNonQuery();

                if(result > 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                throw;
            }

            return false;
        }

        private Dictionary<string, string>? RegisterDelegated(DomainModel model)
        {
            bool atLeastOneNameserver = false;
            Dictionary<string, string> results = new();

            foreach (string NameServer in model.NameServers)
            {
                NewDnsRecord dnsRecord = new()
                {
                    Name = model.DomainName,
                    Content = NameServer,
                    Priority = 0,
                    Proxied = false,
                    Ttl = 1,
                    Type = CloudFlare.Client.Enumerators.DnsRecordType.Ns,
                    Comment = model.AccountId
                };
                var cfresult = s.client.Zones.DnsRecords.AddAsync(model.ZoneId, dnsRecord).Result;

                if (cfresult.Success)
                {
                    atLeastOneNameserver = true;
                }
                else
                {
                    results.Add(NameServer, cfresult.Messages.ToString());
                }
            }

            if (!atLeastOneNameserver)
            {
                return results;
            }
            else
            {
                return null;
            }
        }

        private bool RegisterHosted(DomainModel model, IPAddress iPAddress)
        {
            NewDnsRecord dnsRecord = new()
            {
                Name = model.DomainName,
                Content = iPAddress.ToString(),
                Priority = 0,
                Proxied = false,
                Ttl = 1,
                Type = CloudFlare.Client.Enumerators.DnsRecordType.A,
                Comment = model.AccountId
            };
            var cfresult = s.client.Zones.DnsRecords.AddAsync(model.ZoneId, dnsRecord).Result;
            if (cfresult.Success)
            {
                return true;
            }
            else
            {
                throw new Exception(cfresult.Errors[0].Message);
            }
        }

        public async Task<bool> DeleteUserDomainAsync(string domainName, string zoneId, string zoneName, string accountId)
        {
            if (string.IsNullOrWhiteSpace(domainName)) throw new ArgumentException("domainName required");
            if (string.IsNullOrWhiteSpace(zoneId)) throw new ArgumentException("zoneId required");
            if (string.IsNullOrWhiteSpace(zoneName)) throw new ArgumentException("zoneName required");

            // 1) List all DNS records (paged) via Cloudflare HTTP API
            string baseFqdn = $"{domainName}.{zoneName}".ToLowerInvariant();

            var matches = new List<(string Id, string Name)>();
            int page = 1;
            const int perPage = 100;

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.apikey);

                while (true)
                {
                    var url = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records?page={page}&per_page={perPage}";
                    using var resp = await http.GetAsync(url);
                    resp.EnsureSuccessStatusCode();

                    using var stream = await resp.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    var root = doc.RootElement;

                    // success check
                    if (root.TryGetProperty("success", out var successEl) && !successEl.GetBoolean())
                        throw new Exception("Cloudflare list failed.");

                    // accumulate matches (base + subdomains)
                    if (root.TryGetProperty("result", out var resultArr))
                    {
                        foreach (var r in resultArr.EnumerateArray())
                        {
                            var id = r.GetProperty("id").GetString() ?? "";
                            var name = (r.GetProperty("name").GetString() ?? "").ToLowerInvariant();

                            if (name == baseFqdn || name.EndsWith("." + baseFqdn))
                                matches.Add((id, name));
                        }
                    }

                    // pagination check
                    var ri = root.GetProperty("result_info");
                    int currentPage = ri.GetProperty("page").GetInt32();
                    int per = ri.GetProperty("per_page").GetInt32();
                    int total = ri.GetProperty("total_count").GetInt32();

                    if (currentPage * per >= total) break;
                    page++;
                }

                // 2) Delete all (deepest first)
                foreach (var rec in matches.OrderByDescending(m => m.Name.Length))
                {
                    var delUrl = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records/{rec.Id}";
                    using var delResp = await http.DeleteAsync(delUrl);
                    if (!delResp.IsSuccessStatusCode)
                    {
                        // keep going; optionally log delResp.Content
                    }
                }
            }

            // 3) DB cleanup (best-effort)
            await TryDeleteDomainNameserversAsync(domainName, zoneId);
            await TryDeleteRecordsRowAsync(domainName, zoneId);

            // 4) Remove domain registration row
            bool removedFromDB = await DeleteDomainRegistrationAsync(domainName, zoneId, accountId);
            return removedFromDB;
        }

        public async Task<List<string>> GetRecordsAsync(string DomainName, string ZoneId)
        {
            // Get ZoneName
            var zones = await s.GetAvailableZonesByIdAsync();

            List<string> result = [];

            var dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{DomainName}.{zones[ZoneId]}",
                
            };
            var record = await s.client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter);

            foreach (var item in record.Result)
            {
                result.Add(item.Content);
            }

            return result;
        }


        private async Task<List<string>> GetNameServersAsync(string DomainName, string ZoneId)
        {
            // Get ZoneName
            var zones = await s.GetAvailableZonesByIdAsync();

            List<string> result = [];

            var dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{DomainName}.{zones[ZoneId]}",
                Type = DnsRecordType.Ns
            };
            var record = await s.client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter);
            
            foreach (var item in record.Result)
            {
                result.Add(item.Content);
            }

            return result;
        }


        /// <summary>
        /// Adds a NS record for a delegated subdomain in Cloudflare and stores it in DB (best-effort).
        /// </summary>
        public async Task<(bool Success, string Message)> AddNameserverAsync(
            string domainName, string zoneId, string nameserver, string accountId)
        {
            if (string.IsNullOrWhiteSpace(domainName)) return (false, "DomainName is required.");
            if (string.IsNullOrWhiteSpace(zoneId)) return (false, "ZoneId is required.");
            if (string.IsNullOrWhiteSpace(nameserver)) return (false, "Nameserver is required.");

            // Normalize NS (Cloudflare Content should not end with '.')
            var nsContent = NormalizeNs(nameserver);

            // Optional: avoid duplicates by checking existing NS
            var existing = await GetNameServersAsync(domainName, zoneId);
            if (existing.Any(x => string.Equals(x.TrimEnd('.'), nsContent, StringComparison.OrdinalIgnoreCase)))
                return (true, "Nameserver already present.");

            // Create NS record (same pattern you use in RegisterDelegated)
            var dnsRecord = new NewDnsRecord
            {
                Name = domainName,           // subdomain relative to the zone
                Content = nsContent,         // e.g. ns1.provider.net
                Priority = 0,
                Proxied = false,
                Ttl = 1,                     // Auto
                Type = DnsRecordType.Ns,
                Comment = accountId
            };

            var cfResult = await s.client.Zones.DnsRecords.AddAsync(zoneId, dnsRecord);
            if (!cfResult.Success)
            {
                var err = cfResult.Errors?.FirstOrDefault()?.Message ?? "Cloudflare add failed.";
                return (false, err);
            }

            // Best-effort DB insert (optional). If table doesn't exist, it is safely ignored.
            await TryInsertDomainNameserverAsync(domainName, zoneId, nsContent, accountId);

            return (true, $"Nameserver '{nsContent}.' added.");
        }

        /// <summary>
        /// Removes a NS record for a delegated subdomain in Cloudflare and deletes it from DB (best-effort).
        /// </summary>
        public async Task<(bool Success, string Message)> RemoveNameserverAsync(
            string domainName, string zoneId, string nameserver)
        {
            if (string.IsNullOrWhiteSpace(domainName)) return (false, "DomainName is required.");
            if (string.IsNullOrWhiteSpace(zoneId)) return (false, "ZoneId is required.");
            if (string.IsNullOrWhiteSpace(nameserver)) return (false, "Nameserver is required.");

            var nsContent = NormalizeNs(nameserver);

            // Build full name (subdomain.zone) using zones lookup
            var zonesById = await s.GetAvailableZonesByIdAsync(); // id -> name
            if (!zonesById.TryGetValue(zoneId, out var zoneName))
                return (false, "ZoneId not found.");

            // Find NS records for this subdomain
            var filter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{domainName}.{zoneName}",
                Type = DnsRecordType.Ns
            };

            var list = await s.client.Zones.DnsRecords.GetAsync(zoneId, filter);
            var target = list.Result
                             .FirstOrDefault(r => string.Equals(r.Content?.TrimEnd('.'), nsContent, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return (false, "Nameserver not found.");

            var del = await s.client.Zones.DnsRecords.DeleteAsync(zoneId, target.Id);
            if (!del.Success)
            {
                var err = del.Errors?.FirstOrDefault()?.Message ?? "Cloudflare delete failed.";
                return (false, err);
            }

            // Best-effort DB delete (optional)
            await TryDeleteDomainNameserverAsync(domainName, zoneId, nsContent);

            return (true, $"Nameserver '{nsContent}.' removed.");
        }

        // Listar registros del dominio hosted (incluye subdominios debajo)
        public async Task<List<DnsRecordModel>> GetHostedRecordsAsync(string domainName, string zoneId, string accountId = null)
        {
            var zonesById = await s.GetAvailableZonesByIdAsync();
            if (!zonesById.TryGetValue(zoneId, out var zoneName))
                return new List<DnsRecordModel>();

            string baseFqdn = $"{domainName}.{zoneName}".ToLowerInvariant();

            var all = new List<DnsRecordModel>();
            int page = 1;
            const int perPage = 100;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.apikey);

            while (true)
            {
                var url = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records?page={page}&per_page={perPage}";
                using var resp = await http.GetAsync(url);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;
                if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                    throw new Exception("Cloudflare list failed.");

                var resultArr = root.GetProperty("result");
                foreach (var r in resultArr.EnumerateArray())
                {
                    var name = r.GetProperty("name").GetString() ?? "";
                    var nameLower = name.ToLowerInvariant();

                    if (nameLower == baseFqdn || nameLower.EndsWith("." + baseFqdn))
                    {
                        var type = r.GetProperty("type").GetString() ?? "";
                        var content = r.GetProperty("content").GetString() ?? "";
                        var id = r.GetProperty("id").GetString() ?? "";

                        bool isBaseA = string.Equals(type, "A", StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(nameLower, baseFqdn, StringComparison.OrdinalIgnoreCase);

                        all.Add(new DnsRecordModel
                        {
                            Id = id,
                            Type = type,
                            Name = name,
                            Content = content,
                            Deletable = !isBaseA,
                            IsBaseA = isBaseA
                        });
                    }
                }

                var ri = root.GetProperty("result_info");
                int count = ri.GetProperty("count").GetInt32();
                int total = ri.GetProperty("total_count").GetInt32();
                int currentPage = ri.GetProperty("page").GetInt32();
                int per = ri.GetProperty("per_page").GetInt32();

                if (currentPage * per >= total || count < perPage)
                    break;

                page++;
            }

            return all
                .OrderByDescending(x => x.IsBaseA)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToList();
        }

        // Crear registro Hosted (A, CNAME, TXT)
        public async Task<(bool Ok, string Msg)> CreateHostedRecordAsync(
            string zoneId, string domainName, string type, string inputName, string content, string accountId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return (false, "ZoneId is required.");
            if (string.IsNullOrWhiteSpace(domainName)) return (false, "DomainName is required.");
            if (string.IsNullOrWhiteSpace(type)) return (false, "Type is required.");
            if (string.IsNullOrWhiteSpace(inputName)) return (false, "Name is required.");
            if (string.IsNullOrWhiteSpace(content)) return (false, "Content is required.");

            var zonesById = await s.GetAvailableZonesByIdAsync();
            if (!zonesById.TryGetValue(zoneId, out var zoneName))
                return (false, "Zone not found.");

            string baseFqdn = $"{domainName}.{zoneName}";
            string fqdn = ResolveToFqdn(inputName, baseFqdn, zoneName);
            var cfType = ParseType(type);

            // Validaciones básicas según tipo
            if (cfType == DnsRecordType.A && !System.Net.IPAddress.TryParse(content, out _))
                return (false, "Content must be a valid IPv4 for A records.");
            if (cfType == DnsRecordType.Cname)
            {
                content = NormalizeHost(content);
                if (string.IsNullOrWhiteSpace(content)) return (false, "Content must be a valid hostname for CNAME.");
                // Evitar CNAME en el baseFQDN si ya existe A base (restricción)
                if (string.Equals(fqdn, baseFqdn, StringComparison.OrdinalIgnoreCase))
                    return (false, "Cannot create CNAME on base host because an A base record exists.");
            }

            var newRec = new NewDnsRecord
            {
                Name = fqdn,               // FQDN
                Content = cfType == DnsRecordType.Txt ? content : TrimDot(content),
                Type = cfType,
                Proxied = false,
                Ttl = 1,                   // Auto
                Comment = accountId
            };

            var cf = await s.client.Zones.DnsRecords.AddAsync(zoneId, newRec);
            if (!cf.Success)
                return (false, cf.Errors?.FirstOrDefault()?.Message ?? "Cloudflare add failed.");

            // (Opcional) persistir best-effort en dbo.Records
            await TryUpsertRecordAsync(domainName, zoneId, accountId, cf.Result);

            return (true, $"{type.ToUpperInvariant()} record created.");
        }

        // Eliminar registro Hosted (protegido: no borra el A base)
        public async Task<(bool Ok, string Msg)> DeleteHostedRecordAsync(string zoneId, string domainName, string recordId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return (false, "ZoneId is required.");
            if (string.IsNullOrWhiteSpace(domainName)) return (false, "DomainName is required.");
            if (string.IsNullOrWhiteSpace(recordId)) return (false, "RecordId is required.");

            var zonesById = await s.GetAvailableZonesByIdAsync();
            if (!zonesById.TryGetValue(zoneId, out var zoneName))
                return (false, "Zone not found.");

            string baseFqdn = $"{domainName}.{zoneName}".ToLowerInvariant();

            var details = await s.client.Zones.DnsRecords.GetDetailsAsync(zoneId, recordId);
            if (!details.Success || details.Result == null)
                return (false, "Record not found.");

            var rec = details.Result;
            bool isBaseA = rec.Type == DnsRecordType.A && string.Equals(rec.Name?.ToLowerInvariant(), baseFqdn, StringComparison.OrdinalIgnoreCase);
            if (isBaseA)
                return (false, "The original A record cannot be deleted.");

            var del = await s.client.Zones.DnsRecords.DeleteAsync(zoneId, recordId);
            if (!del.Success)
                return (false, del.Errors?.FirstOrDefault()?.Message ?? "Cloudflare delete failed.");

            await TryDeleteRecordAsync(domainName, zoneId, recordId);

            return (true, "Record deleted.");
        }

        #region Helpers (privados)

        private static string NormalizeNs(string ns)
        {
            var n = ns.Trim();
            if (n.EndsWith(".")) n = n[..^1];
            return n.ToLowerInvariant();
        }

        private async Task TryInsertDomainNameserverAsync(string domainName, string zoneId, string nameserver, string createdBy)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.DomainNameservers WHERE DomainName=@DomainName AND ZoneId=@ZoneId AND Nameserver=@Nameserver)
BEGIN
    INSERT INTO dbo.DomainNameservers (DomainName, ZoneId, Nameserver, CreatedBy)
    VALUES (@DomainName, @ZoneId, @Nameserver, @CreatedBy);
END";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                cmd.Parameters.AddWithValue("@Nameserver", nameserver + ".");
                cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Swallow: table may not exist. Keeping DB logic here
            }
        }

        private async Task TryDeleteDomainNameserverAsync(string domainName, string zoneId, string nameserver)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
DELETE FROM dbo.DomainNameservers
WHERE DomainName=@DomainName AND ZoneId=@ZoneId AND Nameserver=@Nameserver;";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                cmd.Parameters.AddWithValue("@Nameserver", nameserver + ".");

                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Swallow: table may not exist. Keeping DB logic here.
            }
        }

        private static DnsRecordType ParseType(string type)
        {
            return type.Trim().ToUpperInvariant() switch
            {
                "A" => DnsRecordType.A,
                "CNAME" => DnsRecordType.Cname,
                "TXT" => DnsRecordType.Txt,
                _ => throw new ArgumentException("Unsupported type. Only A, CNAME, TXT are allowed.")
            };
        }

        private static string TrimDot(string host)
        {
            var h = (host ?? "").Trim();
            if (h.EndsWith(".")) h = h[..^1];
            return h;
        }

        private static string NormalizeHost(string host)
        {
            var h = TrimDot(host);
            return string.IsNullOrWhiteSpace(h) ? null : h.ToLowerInvariant();
        }

        private static string ResolveToFqdn(string inputName, string baseFqdn, string zoneName)
        {
            var n = (inputName ?? "").Trim();
            if (string.IsNullOrEmpty(n) || n == "@") return baseFqdn;
            if (n.EndsWith(".")) n = n[..^1];

            // si ya es FQDN de la zona, se usa; si no, se anida bajo baseFqdn
            if (n.EndsWith("." + zoneName, StringComparison.OrdinalIgnoreCase)) return n;
            return $"{n}.{baseFqdn}";
        }

        private async Task TryUpsertRecordAsync(string domainName, string zoneId, string accountId, CloudFlare.Client.Api.Zones.DnsRecord.DnsRecord r)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Records WHERE DomainName=@DomainName AND ZoneId=@ZoneId)
BEGIN
    UPDATE dbo.Records SET
        AccountId=@AccountId, RecordContent=@Content, Name=@Name,
        Type=@Type, Comment=@Comment, Id=@Id, TTL=@Ttl, Proxied=@Proxied, Proxiable=@Proxiable, ZonaName=@ZoneName
    WHERE DomainName=@DomainName AND ZoneId=@ZoneId;
END
ELSE
BEGIN
    INSERT INTO dbo.Records (DomainName, ZoneId, AccountId, RecordContent, Name, Proxied, Type, Comment, CreatedOn, Id, Lockef, ModifiedOn, Proxiable, TTL, ZonaName)
    VALUES (@DomainName, @ZoneId, @AccountId, @Content, @Name, @Proxied, @Type, @Comment, @CreatedOn, @Id, NULL, @ModifiedOn, @Proxiable, @Ttl, @ZoneName);
END";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                cmd.Parameters.AddWithValue("@AccountId", accountId ?? "");
                cmd.Parameters.AddWithValue("@Content", (r.Content ?? "").ToString());
                cmd.Parameters.AddWithValue("@Name", r.Name ?? "");
                cmd.Parameters.AddWithValue("@Type", r.Type.ToString());
                //cmd.Parameters.AddWithValue("@Comment", r.Comment ?? "");
                cmd.Parameters.AddWithValue("@Id", r.Id ?? "");
                cmd.Parameters.AddWithValue("@Ttl", r.Ttl);
                cmd.Parameters.AddWithValue("@Proxied", (bool)r.Proxied ? "true" : "false");
                cmd.Parameters.AddWithValue("@Proxiable", "true");
                cmd.Parameters.AddWithValue("@ZoneName", (await s.GetAvailableZonesByIdAsync())[zoneId]);
                cmd.Parameters.AddWithValue("@CreatedOn", DateTime.UtcNow.ToString("s"));
                cmd.Parameters.AddWithValue("@ModifiedOn", DateTime.UtcNow.ToString("s"));
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignorar errores de esquema/truncado (la tabla tiene nchar(10))
            }
        }

        private async Task TryDeleteRecordAsync(string domainName, string zoneId, string recordId)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"UPDATE dbo.Records SET RecordContent=NULL, Name=NULL, Type=NULL, Comment=NULL, Id=NULL WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignorar si la tabla no encaja
            }
        }

        private async Task<bool> DeleteDomainRegistrationAsync(string domainName, string zoneId, string accountId)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM dbo.Domains WHERE DomainName=@DomainName AND ZoneId=@ZoneId AND AccountId=@AccountId";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                cmd.Parameters.AddWithValue("@AccountId", accountId ?? "");
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task TryDeleteDomainNameserversAsync(string domainName, string zoneId)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM dbo.DomainNameservers WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* table may not exist */ }
        }

        private async Task TryDeleteRecordsRowAsync(string domainName, string zoneId)
        {
            try
            {
                using var conn = new SqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM dbo.Records WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* schema may not match; ignore */ }
        }

        #endregion
    }
}

