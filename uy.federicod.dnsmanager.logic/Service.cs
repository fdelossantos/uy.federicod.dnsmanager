using CloudFlare.Client;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.logic
{
    public class Service
    {
        CloudFlareClient client;

        public Service(string username, string apiKey)
        {
            client = new CloudFlareClient(apiKey);
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
