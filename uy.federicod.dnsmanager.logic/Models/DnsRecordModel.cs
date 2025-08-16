using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uy.federicod.dnsmanager.logic.Models
{
    public class DnsRecordModel
    {
        public string Id { get; set; }           // Cloudflare record Id
        public string Type { get; set; }         // A | CNAME | TXT
        public string Name { get; set; }         // FQDN
        public string Content { get; set; }      // IP / hostname / text
        public bool Deletable { get; set; }      // false para el A base
        public bool IsBaseA { get; set; }        // solo informativo
    }
}
