using System;

namespace SW.Bitween.Domain.Metrics;

/// <summary>
/// The broker's totals at one moment, taken every few minutes, so the backlog can be shown as a
/// trend and not only as it is right now. Kept for a month.
/// </summary>
public class QueueSnapshot
{
    /// <summary>The time, cut to the sampling interval: two nodes sampling together keep one.</summary>
    public DateTime At { get; set; }
    public long QueueDepth { get; set; }
    public long RetryBacklog { get; set; }
    public long DeadLetterBacklog { get; set; }
    public double IncomingRate { get; set; }
    public double AckRate { get; set; }
    public int Consumers { get; set; }
    public int UnhealthyConsumers { get; set; }
}
