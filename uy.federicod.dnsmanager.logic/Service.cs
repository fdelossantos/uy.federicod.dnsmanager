using CloudFlare.Client;
using System.Collections;
using System.Data.SqlClient;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.logic
{
    public class Service
    {
        public string DBConnString { get; set; }
        public CloudFlareClient client { get; set; }

        public Service(string username, string apiKey, string dbconnstring)
        {
            client = new CloudFlareClient(apiKey);
            DBConnString = dbconnstring;
        }

        public async Task<IDictionary<string, string>> GetAvailableZonesAsync() {
            Dictionary<string, string> zones = [];
            string query = "SELECT ZoneId, ZoneName FROM dbo.Zones";

            try
            {
                using SqlConnection connection = new(DBConnString);
                connection.Open();

                using SqlCommand command = new(query, connection);
                using SqlDataReader reader = await command.ExecuteReaderAsync();
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

        public async Task<SearchModel> SearchDomainAsync(string Subdomain, string ZoneId)
        {
            SearchModel searchModel = new();
            searchModel.Domain = Subdomain;

            CancellationToken ct = default;

            var zone = await client.Zones.GetDetailsAsync(ZoneId, ct);
            if (zone.Success)
            {
                searchModel.Available = true;
            }
            else
            {
                searchModel.Available = false;
            }
            searchModel.ZoneName = zone.Result.Name;
            searchModel.ZoneId = ZoneId;

            //foreach (var zone in zones.Result)
            //{
            //    var dnsRecords = await client.Zones.DnsRecords.GetAsync(zone.Id, cancellationToken: ct);
            //    foreach (var dnsRecord in dnsRecords.Result)
            //    {
            //        Console.WriteLine(dnsRecord.Name);
            //    }

            //    Console.WriteLine(zone.Name);
            //}

            return searchModel;
        }
    }
}
