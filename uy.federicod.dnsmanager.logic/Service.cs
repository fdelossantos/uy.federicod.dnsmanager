using CloudFlare.Client;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Enumerators;
using System.Collections;
using MySqlConnector;
using System.Text.Json;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.logic
{
    public class Service
    {
        public string DBConnString { get; set; }
        public CloudFlareClient client { get; set; }

        public string apikey { get; set; }

        public Service(string username, string apiKey, string dbconnstring)
        {
            client = new CloudFlareClient(apiKey);
            DBConnString = dbconnstring;
            apikey = apiKey;
        }

        public AccountModel GetAccountOrCreate(string AccountId, string DisplayName)
        {
            AccountModel account = new AccountModel();

            string query = "SELECT * FROM Accounts WHERE AccountId = @AccountId";

            MySqlConnection connection = new(DBConnString);
            connection.Open();

            MySqlCommand command = new(query, connection);
            command.Parameters.AddWithValue("AccountId", AccountId);
            MySqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                account.AccountId = reader["AccountId"].ToString();
                account.DisplayName = reader["DisplayName"].ToString();
                account.Created = (DateTime)reader["Created"];
            }
            reader.Close();

            if (string.IsNullOrEmpty(account.AccountId))
            {
                try
                {
                    DateTime created = DateTime.Now;
                    query = "INSERT INTO Accounts (AccountId, DisplayName, Created) VALUES (@AccountId, @DisplayName, @Created)";
                    MySqlCommand commandc = new(query, connection);
                    commandc.Parameters.AddWithValue("AccountId", AccountId);
                    commandc.Parameters.AddWithValue("DisplayName", DisplayName);
                    commandc.Parameters.AddWithValue("Created", created);

                    int result = commandc.ExecuteNonQuery();

                    account.Created = created;
                    account.AccountId = AccountId;
                    account.DisplayName = DisplayName;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    throw;
                }

            }

            return account;
        }

        public async Task<IDictionary<string, string>> GetAvailableZonesAsync() {
            Dictionary<string, string> zones = [];
            string query = "SELECT ZoneId, ZoneName FROM Zones WHERE Enabled = 1";

            try
            {
                using MySqlConnection connection = new(DBConnString);
                connection.Open();

                using MySqlCommand command = new(query, connection);
                using MySqlDataReader reader = await command.ExecuteReaderAsync();
                while (reader.Read())
                {
                    string zoneName = reader["ZoneName"].ToString();
                    string zoneId = reader["ZoneId"].ToString();
                    zones.Add(zoneName, zoneId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                throw;
            }

            return zones;
        }

        public async Task<IDictionary<string, string>> GetAvailableZonesByIdAsync()
        {
            Dictionary<string, string> zones = [];
            string query = "SELECT ZoneId, ZoneName FROM Zones WHERE Enabled = 1";

            try
            {
                using MySqlConnection connection = new(DBConnString);
                connection.Open();

                using MySqlCommand command = new(query, connection);
                using MySqlDataReader reader = await command.ExecuteReaderAsync();
                while (reader.Read())
                {
                    string zoneName = reader["ZoneName"].ToString();
                    string zoneId = reader["ZoneId"].ToString();
                    zones.Add(zoneId, zoneName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                throw;
            }

            return zones;
        }

        public async Task<SearchModel> SearchDomainAsync(string Subdomain, string ZoneId)
        {
            SearchModel searchModel = new();
            searchModel.Domain = Subdomain;
            CancellationToken ct = default;

            var zone = await client.Zones.GetDetailsAsync(ZoneId, ct);
            searchModel.ZoneName = zone.Result.Name;
            searchModel.ZoneId = ZoneId;

            // Verificar el total de registros DNS con llamada directa a la API
            var totalRecords = await GetTotalRecordsCountAsync(ZoneId);

            // Si ya hay 200 o más registros, marcar como no disponible
            if (totalRecords >= 200)
            {
                searchModel.Available = false;
                searchModel.Message = $"This zone has reached its DNS record limit ({totalRecords}/200).";
                return searchModel;
            }

            // Buscar si está alojado
            var dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{Subdomain}.{zone.Result.Name}",
                Type = DnsRecordType.A
            };
            var record = await client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter);
            if (record.Result.Count > 0)
            {
                searchModel.Available = false;
                searchModel.Message = "This hostname already exists as an A record.";
                return searchModel;
            }

            // Buscar si ya existe como alias Hosted
            dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{Subdomain}.{zone.Result.Name}",
                Type = DnsRecordType.Cname
            };
            record = await client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter);
            if (record.Result.Count > 0)
            {
                searchModel.Available = false;
                searchModel.Message = "This hostname already exists as a CNAME record.";
                return searchModel;
            }

            // Buscar si está delegado
            dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{Subdomain}.{zone.Result.Name}",
                Type = DnsRecordType.Ns
            };
            record = await client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter);
            if (record.Result.Count > 0)
            {
                searchModel.Available = false;
                searchModel.Message = "This hostname already exists as an NS record.";
                return searchModel;
            }

            searchModel.Available = true;
            searchModel.Message = $"This hostname is available. Current DNS records: {totalRecords}/200.";
            return searchModel;
        }

        private async Task<int> GetTotalRecordsCountAsync(string zoneId)
        {
            try
            {
                using var httpClient = new HttpClient();

                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apikey}");

                // Llamada para obtener solo el conteo (per_page=1 para eficiencia)
                var url = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records?per_page=1";
                var response = await httpClient.GetStringAsync(url);

                // Parse JSON para obtener el conteo total
                using var jsonDoc = JsonDocument.Parse(response);
                if (jsonDoc.RootElement.TryGetProperty("result_info", out var resultInfo) &&
                    resultInfo.TryGetProperty("total_count", out var totalCount))
                {
                    return totalCount.GetInt32();
                }

                // Si no encontramos el conteo, usar fallback conservador
                return 190;
            }
            catch (Exception ex)
            {
                // Fallback conservador - asumimos que está cerca del límite para ser seguros
                return 190;
            }
        }
    }
}
