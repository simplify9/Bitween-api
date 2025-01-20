using System;

namespace SW.Bitween.Model
{
    public class XchangeResultNotification
    {
        public string Id { get; set; }
        public bool Success { get; set; }
        public string Exception { get; set; }
        public DateTime FinishedOn { get; set; }
        public bool OutputBad { get; set; }
        public bool ResponseBad { get; set; }
        public string DocumentName { get; set; }
        public int DocumentId { get; set; }
        public int SubscriptionId { get; set; }
        public string SubscriptionName { get; set; }
        public string CorrelationId { get; set; }
        public DateTime StartedOn { get; set; }
    }
}