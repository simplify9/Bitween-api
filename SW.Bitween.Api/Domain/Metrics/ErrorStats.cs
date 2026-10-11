using System;

namespace SW.Bitween.Domain.Metrics;

/// <summary>
/// How often one kind of error happened in an hour, for an adapter, a subscription or a gateway.
/// Errors are told apart by a fingerprint of their message with the parts that change every time
/// (ids, numbers, dates) taken out, so "Order 1043 not found" and "Order 2210 not found" count as
/// one, with the latest whole message kept as an example.
/// </summary>
public class ErrorStats
{
    public const string AdapterScope = "adapter";
    public const string SubscriptionScope = "subscription";
    public const string GatewayScope = "gateway";

    public DateTime Hour { get; set; }
    public string Node { get; set; }
    public string Scope { get; set; }
    /// <summary>The adapter id, subscription id or gateway url name.</summary>
    public string ScopeKey { get; set; }
    public string Fingerprint { get; set; }
    public string Sample { get; set; }
    public int Count { get; set; }
    public DateTime LastSeenOn { get; set; }
}
