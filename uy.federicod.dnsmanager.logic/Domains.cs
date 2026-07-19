using CloudFlare.Client.Api.Zones;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Client.Zones;
using CloudFlare.Client.Enumerators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
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

            MySqlConnection connection = new(s.DBConnString);
            connection.Open();

            MySqlCommand command = new(query, connection);
            command.Parameters.AddWithValue("AccountId", AccountId);

            MySqlDataReader reader = command.ExecuteReader();
            while(reader.Read())
            {
                results.Add(new DomainModel()
                {
                    AccountId = AccountId,
                    DelegationType = reader["DelegationType"].ToString(),
                    HostedRecordType = reader["HostedRecordType"] == DBNull.Value
                        ? null
                        : reader["HostedRecordType"].ToString(),
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

            MySqlConnection connection = new(s.DBConnString);
            connection.Open();

            MySqlCommand command = new(query, connection);
            command.Parameters.AddWithValue("DomainName", DomainName);
            command.Parameters.AddWithValue("ZoneId", ZoneId);
            command.Parameters.AddWithValue("AccountId", AccountId);

            MySqlDataReader reader = command.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                result = new DomainModel()
                {
                    AccountId = AccountId,
                    DelegationType = reader["DelegationType"].ToString(),
                    HostedRecordType = reader["HostedRecordType"] == DBNull.Value
                        ? null
                        : reader["HostedRecordType"].ToString(),
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

        public async Task<(bool Ok, string Message)> CreateAsync(
            DomainRegistrationRequest request,
            AccountModel accountModel)
        {
            AccountModel realAccount = s.GetAccountOrCreate(accountModel.AccountId, accountModel.DisplayName);

            bool isHosted = string.Equals(request.DelegationType, "Hosted", StringComparison.Ordinal);
            bool isDelegated = string.Equals(request.DelegationType, "Delegated", StringComparison.Ordinal);
            if (!isHosted && !isDelegated)
            {
                return (false, "Choose how DNS should be managed.");
            }

            string? normalizedRecordType = null;
            string? normalizedTarget = null;
            if (isHosted)
            {
                if (!HostedRecordRules.TryNormalizeRecordType(request.HostedRecordType, out normalizedRecordType))
                {
                    return (false, "Choose A or CNAME as the base record type.");
                }

                string baseFqdn = $"{request.DomainName}.{request.ZoneName}";
                if (!HostedRecordRules.TryNormalizeTarget(
                        normalizedRecordType,
                        request.HostedTarget,
                        baseFqdn,
                        out normalizedTarget,
                        out var validationError))
                {
                    return (false, validationError);
                }
            }
            else if (request.NameServers.Count == 0 ||
                     request.NameServers.Any(nameserver =>
                         !HostedRecordRules.TryNormalizeHostname(nameserver, out _)))
            {
                return (false, "Enter at least one valid fully qualified name server.");
            }

            DomainModel model = new()
            {
                DomainName = request.DomainName,
                ZoneId = request.ZoneId,
                AccountId = accountModel.AccountId,
                DelegationType = request.DelegationType,
                HostedRecordType = normalizedRecordType,
                NameServers = request.NameServers.ToList()
            };

            if (!AddToDB(model, realAccount))
            {
                return (false, "The domain registration could not be saved.");
            }

            if (isHosted)
            {
                try
                {
                    await RegisterHostedAsync(model, normalizedTarget!);
                    return (true, $"{normalizedRecordType} record created.");
                }
                catch (Exception ex)
                {
                    await DeleteDomainRegistrationAsync(
                        model.DomainName,
                        model.ZoneId,
                        model.AccountId);
                    return (false, ex.Message);
                }
            }

            Dictionary<string, string>? delegatedResults = RegisterDelegated(model);
            if (delegatedResults is null)
            {
                return (true, "Delegation created.");
            }

            return (false, delegatedResults.Values.FirstOrDefault() ?? "The delegation could not be created.");
        }

        private bool AddToDB(DomainModel domainModel, AccountModel accountModel)
        {
            try
            {
                string query = @"INSERT INTO Domains
                    (DomainName, ZoneId, AccountId, DelegationType, HostedRecordType)
                    VALUES (@DomainName, @ZoneId, @AccountId, @DelegationType, @HostedRecordType)";
                MySqlConnection connection = new(s.DBConnString);
                connection.Open();

                MySqlCommand command = new(query, connection);
                command.Parameters.AddWithValue("DomainName", domainModel.DomainName);
                command.Parameters.AddWithValue("ZoneId", domainModel.ZoneId);
                command.Parameters.AddWithValue("AccountId", accountModel.AccountId);
                command.Parameters.AddWithValue("DelegationType", domainModel.DelegationType);
                command.Parameters.AddWithValue(
                    "HostedRecordType",
                    (object?)domainModel.HostedRecordType ?? DBNull.Value);

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

        private async Task RegisterHostedAsync(DomainModel model, string normalizedTarget)
        {
            NewDnsRecord dnsRecord = HostedRecordRules.BuildBaseRecord(
                model.DomainName,
                model.HostedRecordType!,
                normalizedTarget,
                model.AccountId);

            var cfResult = await s.client.Zones.DnsRecords.AddAsync(model.ZoneId, dnsRecord);
            if (!cfResult.Success)
            {
                throw new Exception(
                    cfResult.Errors?.FirstOrDefault()?.Message ??
                    "Cloudflare rejected the hosted record.");
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

                        bool isBaseRecord = HostedRecordRules.IsBaseRecord(type, nameLower, baseFqdn);

                        all.Add(new DnsRecordModel
                        {
                            Id = id,
                            Type = type,
                            Name = name,
                            Content = content,
                            Deletable = !isBaseRecord,
                            IsBaseRecord = isBaseRecord
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
                .OrderByDescending(x => x.IsBaseRecord)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToList();
        }

        // Crear registro Hosted (A, CNAME, TXT, MX)
        public async Task<(bool Ok, string Msg)> CreateHostedRecordAsync(
            string zoneId, string domainName, string type, string inputName, string content, string accountId, string recordPriority = null)
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

            if (!HostedRecordRules.IsWithinDomainTree(fqdn, baseFqdn))
                return (false, "The record name must be the hosted domain or one of its descendants.");

            var registration = await GetHostedRegistrationAsync(domainName, zoneId, accountId);
            if (!registration.Exists)
                return (false, "The hosted domain was not found for the current user.");

            bool isBaseName = string.Equals(fqdn, baseFqdn, StringComparison.OrdinalIgnoreCase);
            if (isBaseName && string.Equals(
                    registration.HostedRecordType,
                    HostedRecordRules.CnameType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (false, "A CNAME-based domain cannot have additional records at the base hostname.");
            }

            // Validaciones básicas según tipo
            if (cfType == DnsRecordType.A &&
                (!System.Net.IPAddress.TryParse(content, out var address) ||
                 address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
                return (false, "Content must be a valid IPv4 for A records.");
            if (cfType == DnsRecordType.Cname)
            {
                if (!HostedRecordRules.TryNormalizeHostname(content, out var normalizedHostname))
                    return (false, "Content must be a valid fully qualified hostname for CNAME.");
                content = normalizedHostname;
                // Evitar CNAME en el baseFQDN si ya existe A base (restricción)
                if (isBaseName)
                    return (false, "Cannot create CNAME on base host because an A base record exists.");
            }

            int priorityValue = 0;
            if (cfType == DnsRecordType.Mx)
            {
                // Parse and validate priority
                if (!int.TryParse(recordPriority, out priorityValue) || priorityValue < 0)
                {
                    priorityValue = 10; // default priority
                }

                // Normalize MX content (mail exchanger host)
                content = NormalizeHost(content);
                if (string.IsNullOrWhiteSpace(content)) return (false, "Content must be a valid hostname for MX.");
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

            if (cfType == DnsRecordType.Mx)
            {
                newRec.Priority = priorityValue;
            }

            var cf = await s.client.Zones.DnsRecords.AddAsync(zoneId, newRec);
            if (!cf.Success)
                return (false, cf.Errors?.FirstOrDefault()?.Message ?? "Cloudflare add failed.");

            // (Opcional) persistir best-effort en Records
            await TryUpsertRecordAsync(domainName, zoneId, accountId, cf.Result);

            return (true, $"{type.ToUpperInvariant()} record created.");
        }

        // Eliminar registro Hosted (protegido: no borra el A base)
        public async Task<(bool Ok, string Msg)> DeleteHostedRecordAsync(
            string zoneId,
            string domainName,
            string recordId,
            string accountId)
        {
            if (string.IsNullOrWhiteSpace(zoneId)) return (false, "ZoneId is required.");
            if (string.IsNullOrWhiteSpace(domainName)) return (false, "DomainName is required.");
            if (string.IsNullOrWhiteSpace(recordId)) return (false, "RecordId is required.");

            var zonesById = await s.GetAvailableZonesByIdAsync();
            if (!zonesById.TryGetValue(zoneId, out var zoneName))
                return (false, "Zone not found.");

            string baseFqdn = $"{domainName}.{zoneName}".ToLowerInvariant();

            var registration = await GetHostedRegistrationAsync(domainName, zoneId, accountId);
            if (!registration.Exists)
                return (false, "The hosted domain was not found for the current user.");

            var details = await s.client.Zones.DnsRecords.GetDetailsAsync(zoneId, recordId);
            if (!details.Success || details.Result == null)
                return (false, "Record not found.");

            var rec = details.Result;
            if (!HostedRecordRules.IsWithinDomainTree(rec.Name, baseFqdn))
                return (false, "The record does not belong to the hosted domain.");

            if (HostedRecordRules.IsBaseRecord(rec.Type.ToString(), rec.Name, baseFqdn))
                return (false, "The original hosted record cannot be deleted individually.");

            var del = await s.client.Zones.DnsRecords.DeleteAsync(zoneId, recordId);
            if (!del.Success)
                return (false, del.Errors?.FirstOrDefault()?.Message ?? "Cloudflare delete failed.");

            await TryDeleteRecordAsync(domainName, zoneId, recordId);

            return (true, "Record deleted.");
        }

        #region Helpers (privados)

        private async Task<(bool Exists, string? HostedRecordType)> GetHostedRegistrationAsync(
            string domainName,
            string zoneId,
            string accountId)
        {
            using var connection = new MySqlConnection(s.DBConnString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT HostedRecordType
FROM Domains
WHERE DomainName = @DomainName
  AND ZoneId = @ZoneId
  AND AccountId = @AccountId
  AND DelegationType = 'Hosted'";
            command.Parameters.AddWithValue("@DomainName", domainName);
            command.Parameters.AddWithValue("@ZoneId", zoneId);
            command.Parameters.AddWithValue("@AccountId", accountId);

            object? value = await command.ExecuteScalarAsync();
            if (value is null)
            {
                return (false, null);
            }

            string hostedRecordType = value == DBNull.Value
                ? HostedRecordRules.AddressType
                : value.ToString() ?? HostedRecordRules.AddressType;
            return (true, hostedRecordType);
        }

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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO DomainNameservers (DomainName, ZoneId, Nameserver, CreatedBy)
VALUES (@DomainName, @ZoneId, @Nameserver, @CreatedBy)
ON DUPLICATE KEY UPDATE CreatedBy = VALUES(CreatedBy);";
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
DELETE FROM DomainNameservers
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
                "MX" => DnsRecordType.Mx,
                _ => throw new ArgumentException("Unsupported type. Only A, CNAME, TXT, MX are allowed.")
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO Records (DomainName, ZoneId, AccountId, RecordContent, Name, Proxied, Type, Comment, CreatedOn, Id, Lockef, ModifiedOn, Proxiable, TTL, ZonaName)
VALUES (@DomainName, @ZoneId, @AccountId, @Content, @Name, @Proxied, @Type, @Comment, @CreatedOn, @Id, NULL, @ModifiedOn, @Proxiable, @Ttl, @ZoneName)
ON DUPLICATE KEY UPDATE
    AccountId = VALUES(AccountId),
    RecordContent = VALUES(RecordContent),
    Name = VALUES(Name),
    Proxied = VALUES(Proxied),
    Type = VALUES(Type),
    Comment = VALUES(Comment),
    ModifiedOn = VALUES(ModifiedOn),
    Id = VALUES(Id),
    Proxiable = VALUES(Proxiable),
    TTL = VALUES(TTL),
    ZonaName = VALUES(ZonaName);";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                cmd.Parameters.AddWithValue("@AccountId", accountId ?? "");
                cmd.Parameters.AddWithValue("@Content", (r.Content ?? "").ToString());
                cmd.Parameters.AddWithValue("@Name", r.Name ?? "");
                cmd.Parameters.AddWithValue("@Type", r.Type.ToString());
                cmd.Parameters.AddWithValue("@Comment", r.Comment ?? "");
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"UPDATE Records SET RecordContent=NULL, Name=NULL, Type=NULL, Comment=NULL, Id=NULL WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM Domains WHERE DomainName=@DomainName AND ZoneId=@ZoneId AND AccountId=@AccountId";
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM DomainNameservers WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
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
                using var conn = new MySqlConnection(s.DBConnString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM Records WHERE DomainName=@DomainName AND ZoneId=@ZoneId";
                cmd.Parameters.AddWithValue("@DomainName", domainName);
                cmd.Parameters.AddWithValue("@ZoneId", zoneId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* schema may not match; ignore */ }
        }

        #endregion
    }
}

