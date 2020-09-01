using SW.Infolink;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink
{
    public class XchangeRow
    {
        public int Id { get; set; }
        public int SubscriberId { get; set; }
        public string SubscriberName { get; set; }
        public int DocumentId { get; set; }
        public string DocumentName { get; set; }
        public string HandlerId { get; set; }
        //public string HandlerName { get; set; }
        public string MapperId { get; set; }
        //public string MapperName { get; set; }
        public IEnumerable<string> References { get; set; }
        public XchangeStatus  Status { get; set; }
        public string StatusString { get; set; }
        public string Exception { get; set; }
        public DateTime? DeliveredOn { get; set; }
        public DateTime? FinishedOn { get; set; }
        public DateTime StartedOn { get; set; }
        public string InputFileName { get; set; }
        public int InputFileSize { get; set; }
        public string InputFileHash { get; set; }
        public DateTime? DeliverOn { get; set; }
    }
}
