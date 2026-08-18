using System;

namespace SW.Bitween.Model;

/// <summary>
/// The JSON handed to an alert handler when a retry group's shared budget runs out for one
/// subscription, meaning failures matching that group have stopped being retried.
/// </summary>
/// <remarks>
/// Sent once per subscription and group, and not again until the budget is reset — unlike
/// <see cref="XchangeResultNotification"/>, which is sent per exchange.
/// </remarks>
public class RetryBudgetExhaustedNotification
{
    /// <summary>The failure that found the budget empty.</summary>
    public string XchangeId { get; set; }

    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; }
    public string DocumentName { get; set; }
    public string CorrelationId { get; set; }

    /// <summary>Null when the subscription uses an inline policy rather than a named one.</summary>
    public string PolicyName { get; set; }

    /// <summary>The group whose budget is spent — the condition that has stopped being retried.</summary>
    public string GroupName { get; set; }

    /// <summary>The ceiling that was reached.</summary>
    public int MaxAttemptsTotal { get; set; }

    /// <summary>The policy's own words for why this failure was refused.</summary>
    public string BlockedReason { get; set; }

    /// <summary>The failure text of the exchange that hit the empty budget.</summary>
    public string Exception { get; set; }

    public DateTime OccurredOn { get; set; }
}
