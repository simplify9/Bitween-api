using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{
    public class ReceiverConfig
    {
        public string Name { get; set; }
        public string ReceiverId { get; set; }
        public IDictionary<string, string> Properties { get; set; }
        public ICollection<Schedule> Schedules { get;set; }
    }
}
