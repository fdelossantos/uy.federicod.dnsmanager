using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uy.federicod.dnsmanager.logic.Models
{
    public class NameserverChangeModel
    {
        public string ZoneName { get; set; }      // e.g. "example.edu"
        public string DomainName { get; set; }    // e.g. "student01"
        public string Nameserver { get; set; }    // e.g. "ns1.provider.net."
    }
}