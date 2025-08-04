using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uy.federicod.dnsmanager.logic.Models
{
    public class SearchModel
    {
        public string Domain { get; set; }
        public bool Available { get; set; }
        public string ZoneName { get; set; }
        public string ZoneId { get; set; }
        public string Message { get; set; }

    }
}
