using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audit_final.Models
{
    public class AuditRecord
    {
        public DateTime EventTime { get; set; }
        public string ActionId { get; set; }
        public bool Succeeded { get; set; }
        public string User { get; set; }
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Object { get; set; }
        public string Statement { get; set; }
        //public long SequenceNumber { get; set; }
    }
}
