using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class DelayedRetryRow
{
    public string Id { get; set; } = null!;
    public DateTime On { get; set; }

    /// <summary>
    /// Kept because its group's retry budget is spent: <see cref="On"/> is a placeholder, and it runs
    /// once the subscription next succeeds or the budget is reset.
    /// </summary>
    public bool Held { get; set; }
    public int? SubscriptionId { get; set; }
    /// <summary>Null alongside a null SubscriptionId.</summary>
    public string? SubscriptionName { get; set; }
    public int DocumentId { get; set; }
    public string DocumentName { get; set; } = null!;
    /// <summary>Why the attempt this retry follows failed.</summary>
    public string? Exception { get; set; }
    public DateTime StartedOn { get; set; }

    /// <summary>
    /// The exchange's promoted properties, so a pending retry can identify itself
    /// by what it carries (order number, store…) instead of only by its id.
    /// Null when the document type promotes nothing.
    /// </summary>
    public IDictionary<string, string>? PromotedProperties { get; set; }

    /// <summary>
    /// The shared retry policy the subscription currently points at. Null when the
    /// subscription carries an inline <c>CustomRetryPolicy</c> instead - a delayed retry
    /// can only be scheduled by one or the other, so null means "look on the subscription".
    /// Reflects the policy as it stands now, not necessarily the one that set <see cref="On"/>.
    /// </summary>
    public int? RetryPolicyId { get; set; }

    public string? RetryPolicyName { get; set; }
}

public class DelayedRetryRunNow
{
}
