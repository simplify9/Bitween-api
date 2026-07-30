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

    /// <summary>
    /// The shared retry policy the subscription currently points at. Null when the
    /// subscription carries an inline <c>CustomRetryPolicy</c> instead — a delayed retry
    /// can only be scheduled by one or the other, so null means "look on the subscription".
    /// Reflects the policy as it stands now, not necessarily the one that set <see cref="On"/>.
    /// </summary>
    public int? RetryPolicyId { get; set; }

    public string RetryPolicyName { get; set; }
}

public class DelayedRetryRunNow
{
}
