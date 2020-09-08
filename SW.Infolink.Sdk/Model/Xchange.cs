using SW.Infolink;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class XchangeRow
    {
        public string Id { get; set; }
        public int? SubscriptionId { get; set; }
        public string SubscriptionName { get; set; }
        public int DocumentId { get; set; }
        public string DocumentName { get; set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public IEnumerable<string> References { get; set; }
        public bool  Status { get; set; }
        public string StatusString { get; set; }
        public string Exception { get; set; }
        public DateTime? DeliveredOn { get; set; }
        public DateTime? FinishedOn { get; set; }
        public DateTime StartedOn { get; set; }
        public string InputFileName { get; set; }
        public int InputFileSize { get; set; }
        public string InputFileHash { get; set; }
        public DateTime? DeliverOn { get; set; }
        public string InputUrl { get; set; }
        public string OutputUrl { get; set; }
        public string ResponseUrl { get; set; }
        public string Duration { get; set; }

    }
}
