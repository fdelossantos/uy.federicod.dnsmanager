using CloudFlare.Client.Api.Zones;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Client.Zones;
using CloudFlare.Client.Enumerators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
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

        public DomainModel? GetUserDomain(string DomainName, string ZoneId , string AccountId)
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
            
            if(count > 0)
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

        public bool DeleteUserDomain(string DomainName, string ZoneId, string ZoneName, string AccountId)
        {
            bool removedFromDB = false;
            DomainModel domain = GetUserDomain(DomainName, ZoneId, AccountId);

            // Remove from DB
            try
            {
                string query = "DELETE FROM Domains WHERE DomainName = @DomainName AND ZoneId = @ZoneId AND AccountId = @AccountId";

                SqlConnection connection = new(s.DBConnString);
                connection.Open();

                SqlCommand command = new(query, connection);
                command.Parameters.AddWithValue("DomainName", DomainName);
                command.Parameters.AddWithValue("ZoneId", ZoneId);
                command.Parameters.AddWithValue("AccountId", AccountId);

                int result = command.ExecuteNonQuery();

                if (result > 0)
                {
                    removedFromDB = true;
                }
            }
            catch (Exception)
            {
                throw;
            }

            // Remove from CF

            var dnsRecordFilter = new DnsRecordFilter
            {
                Match = CloudFlare.Client.Enumerators.MatchType.All,
                Name = $"{DomainName}.{ZoneName}"
            };

            var rr = s.client.Zones.DnsRecords.GetAsync(ZoneId, dnsRecordFilter).Result;
            foreach (var item in rr.Result)
            {
                var cfresult = s.client.Zones.DnsRecords.DeleteAsync(ZoneId, item.Id);
            }

            return true;
        }
    }
}
