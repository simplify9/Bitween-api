using System;

namespace SW.Bitween.Domain;

/// <summary>
/// Running total of the retries one retry group has spent for one integration, backing
/// <c>RetryBudget.MaxAttemptsTotal</c>.  That cap is shared by every message hitting the
/// group, so it cannot be tracked on an individual xchange.
/// </summary>
/// <remarks>
/// The total never resets on its own: once <see cref="AttemptsUsed"/> reaches the group's
/// <c>MaxAttemptsTotal</c> the group stops retrying for that integration until this row is
/// cleared.
/// </remarks>
public class RetryGroupUsage
{
    /// <summary>The integration whose budget this is. A shared policy gives each one its own total.</summary>
    public int SubscriptionId { get; set; }

    /// <summary><c>RetryGroup.Id</c>, which survives policy edits, so the total does too.</summary>
    public Guid GroupId { get; set; }

    public int AttemptsUsed { get; set; }

    /// <summary>When the last attempt was claimed — the only clue left once a group is exhausted.</summary>
    public DateTime LastAttemptOn { get; set; }

    /// <summary>
    /// When the exhaustion alert for this integration and group was claimed, or <c>null</c> while
    /// the budget still has room.
    /// </summary>
    /// <remarks>
    /// Claiming this is what makes the alert fire exactly once: every failure after the budget runs
    /// out would otherwise raise another one. Reset deletes the whole row, which re-arms the alert
    /// along with the budget.
    /// </remarks>
    public DateTime? ExhaustedNotifiedOn { get; set; }
}
