using System;

namespace SW.Bitween.Domain.Metrics;

/// <summary>
/// One hour of exchanges for one subscription and partner, as one node processed them. Counted in
/// memory as each exchange finishes and added in here once a minute (see MetricsFlusher), so no
/// page ever counts exchanges themselves. Hours are those the exchanges started in.
/// </summary>
/// <remarks>
/// A node's rows carry its name, so nodes never write the same row. History from before metrics
/// were recorded is folded in once under <see cref="HistoryNode"/>.
/// </remarks>
public class ExchangeStats : TimedStats
{
    /// <summary>The node name history rows are written under.</summary>
    public const string HistoryNode = "(history)";

    /// <summary>0 when there is none: an exchange routed by information type alone.</summary>
    public const int None = 0;

    /// <summary>The subscription and partner of the row that marks an hour of history as done.</summary>
    public const int HourDone = -1;

    public DateTime Hour { get; set; }
    public string Node { get; set; }
    public int SubscriptionId { get; set; }
    public int PartnerId { get; set; }

    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int BadResponse { get; set; }
    public int Failed { get; set; }
    public int FailedAtMapper { get; set; }
    public int FailedAtHandler { get; set; }
    /// <summary>Exchanges that were themselves retries of another.</summary>
    public int Retries { get; set; }
    public int ManualRetries { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }

    public void Add(ExchangeStats other)
    {
        Total += other.Total;
        Succeeded += other.Succeeded;
        BadResponse += other.BadResponse;
        Failed += other.Failed;
        FailedAtMapper += other.FailedAtMapper;
        FailedAtHandler += other.FailedAtHandler;
        Retries += other.Retries;
        ManualRetries += other.ManualRetries;
        BytesIn += other.BytesIn;
        BytesOut += other.BytesOut;
        AddTimes(other);
    }
}
