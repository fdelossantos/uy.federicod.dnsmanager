using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uy.federicod.dnsmanager.logic.Models
{
    public class DomainModel
    {
        public string DomainName { get; set; }
        public string AccountId { get; set; }
        public string ZoneId { get; set; }
        public string DelegationType { get; set; } // Delegated | Hosted
        public List<string>? NameServers { get; set; }

        public DomainModel()
        {
            DelegationType = "Hosted";
        }
    }
}
