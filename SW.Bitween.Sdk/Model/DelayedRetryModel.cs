using System;

namespace SW.Bitween.Model;

public class DelayedRetryRow
{
    public string Id { get; set; }
    public DateTime On { get; set; }
    public int? SubscriptionId { get; set; }
    public string SubscriptionName { get; set; }
    public int DocumentId { get; set; }
    public string DocumentName { get; set; }
    public string Exception { get; set; }
    public DateTime StartedOn { get; set; }
}

public class DelayedRetryRunNow
{
}
