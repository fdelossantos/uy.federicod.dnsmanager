using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Client.Zones;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.logic
{
    public class Domains
    {
        Service s;
        public Domains() { }

        public Domains(Service service)
        { 
            s = service;
        }

        public async Task<DomainModel?> CreateAsync(string DomainName, string ZoneId, string AccountId, IPAddress iPAddress)
        {
            DomainModel model = new DomainModel();
            string query = "INSERT INTO dbo.Domains (DomainName, ZoneId, AccountId) VALUES (@DomainName, @ZoneId, @AccountId)";

            try
            {
                using SqlConnection connection = new(s.DBConnString);
                connection.Open();

                using SqlCommand command = new(query, connection);
                command.Parameters.AddWithValue("DomainName", DomainName);
                command.Parameters.AddWithValue("ZoneId", ZoneId);
                command.Parameters.AddWithValue("AccountId", AccountId);

                await command.ExecuteNonQueryAsync();

                NewDnsRecord dnsRecord = new()
                {
                    Name = DomainName,
                    Content = iPAddress.ToString(),
                    Priority = 0,
                    Proxied = false,
                    Ttl = 1,
                    Type = CloudFlare.Client.Enumerators.DnsRecordType.A,
                    Comment = AccountId
                };
                var cfresult = await s.client.Zones.DnsRecords.AddAsync(ZoneId, dnsRecord);
                if(cfresult.Success)
                {
                    return model;
                }
                else
                {
                    throw new Exception(cfresult.Errors[0].Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                throw;
            }

        }
    }
}
