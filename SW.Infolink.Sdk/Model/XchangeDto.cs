
using System;
using System.Security.Cryptography;
using System.Text;

namespace SW.Infolink
{
    public class XchangeDto 
    {
        public int Id { get; set; }
        public int SubscriberId { get; set; }
        public int DocumentId { get; set; }
        public int HandlerId { get; set; }
        public int MapperId { get; set; }
        public string[] References { get; set; }
        public string HostName { get; set; }
        public DateTime? DeliveredOn { get; set; }
        public DateTime StartedOn { get; set; }
    }
}
