using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class RetryPolicyCreate
{
    public required string Name { get; set; }
    public List<RetryGroup> Groups { get; set; } = [];

    /// <summary>
    /// Default destination for budget-exhausted alerts, inherited by every group that does not
    /// override it. Null means no alert unless a group or a subscription+group override sets one.
    /// </summary>
    public string? AlertHandlerId { get; set; }

    /// <summary>That adapter's own settings — api key, recipients, subject.</summary>
    public Dictionary<string, string>? AlertHandlerProperties { get; set; }
}

public class RetryPolicyUpdate : RetryPolicyCreate { }

public class RetryPolicyRow
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int GroupCount { get; set; }
}

/// <summary>
/// The whole state of one subscription-and-group pair under a policy: how much of the group's
/// <see cref="RetryBudget.MaxAttemptsTotal"/> that subscription has spent, and where the pair's
/// budget-exhausted alert goes.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are keyed by the same <c>(SubscriptionId, GroupId)</c> pair, which is why they
/// travel together rather than in two reports the reader has to join by eye: the question asked
/// when a budget runs out is "did anyone get told?", and that needs both.
/// </para>
/// <para>
/// A row exists for every pair, including subscriptions that have never failed — an alert override
/// has to be configurable before the first failure, not after. Those rows carry the group's ceiling
/// with nothing spent against it, and a null <see cref="LastAttemptOn"/>.
/// </para>
/// </remarks>
public class RetryGroupUsageRow
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; }

    public int AttemptsUsed { get; set; }
    public int MaxAttemptsTotal { get; set; }

    /// <summary>True when the budget is spent and this subscription will get no further retries.</summary>
    public bool Exhausted { get; set; }

    /// <summary>
    /// Null when this pair has never failed, which is also how a caller knows there is no counter
    /// to reset for it.
    /// </summary>
    public DateTime? LastAttemptOn { get; set; }

    /// <summary>
    /// When the exhaustion alert was raised, or null if the budget still has room — or ran out
    /// before alerts existed.
    /// </summary>
    public DateTime? ExhaustedNotifiedOn { get; set; }

    /// <summary>This pair's own override mode. <c>Inherit</c> when no override row exists.</summary>
    public RetryAlertMode AlertMode { get; set; }

    /// <summary>The override's handler, when it defines one. Not the resolved handler.</summary>
    public string? OverrideHandlerId { get; set; }

    /// <summary>That override's own settings.</summary>
    public Dictionary<string, string>? OverrideHandlerProperties { get; set; }

    /// <summary>Where the alert actually goes, or null when nothing sends for this pair.</summary>
    public string? ResolvedHandlerId { get; set; }

    /// <summary>
    /// The winning level's own settings. Carried so that overriding an inherited alert can start
    /// from what it currently sends: an override replaces rather than merges, so a handler copied
    /// without its properties would save an override that fails at send time.
    /// </summary>
    public Dictionary<string, string>? ResolvedHandlerProperties { get; set; }

    /// <summary>Which level supplied <see cref="ResolvedHandlerId"/>, or null when nothing sends.</summary>
    public RetryAlertLevel? ResolvedFrom { get; set; }

    /// <summary>
    /// Which level deliberately switched this pair's alert off, when one did. Resolution returns
    /// nothing in that case exactly as it does when no level ever configured an alert, and the two
    /// need telling apart: one is a decision, the other is an oversight.
    /// </summary>
    public RetryAlertLevel? SilencedAt { get; set; }
}

/// <summary>
/// Creates, changes or clears the alert override for one subscription and group. Sending
/// <see cref="RetryAlertMode.Inherit"/> removes the override rather than storing a row that does
/// nothing.
/// </summary>
public class RetryAlertOverrideSave
{
    public int SubscriptionId { get; set; }
    public Guid GroupId { get; set; }
    public RetryAlertMode AlertMode { get; set; }
    public string? AlertHandlerId { get; set; }
    public Dictionary<string, string>? AlertHandlerProperties { get; set; }
}

/// <summary>Empty request body — the policy is identified by the route key.</summary>
public class RetryPolicyUsageRequest
{
}

/// <summary>
/// Clears spent budget so a group starts retrying again. Omit both fields to reset every
/// subscription and group of the policy.
/// </summary>
public class RetryPolicyResetUsage
{
    public int? SubscriptionId { get; set; }
    public Guid? GroupId { get; set; }
}

/// <summary>
/// Simulates evaluating a (possibly unsaved/draft) set of retry groups against a single
/// failure, across as many consecutive attempts as requested, so the management UI can
/// show "what would happen" before the policy is saved.
/// </summary>
public class TestRetryPolicyRequest
{
    /// <summary>The draft groups to test — not necessarily the persisted policy's groups.</summary>
    public List<RetryGroup> Groups { get; set; } = [];

    /// <summary>Error or BadResult — Success is never retried and is rejected.</summary>
    public XchangeResultType ResultType { get; set; }

    /// <summary>Exception text for Error, or the raw JSON response body for BadResult.</summary>
    public required string Content { get; set; }

    /// <summary>How many consecutive failed attempts of this same message to simulate.</summary>
    public int AttemptsToSimulate { get; set; } = 5;
}

public class TestRetryPolicyResponse
{
    public List<TestRetryAttemptResult> Attempts { get; set; } = [];
}

public class TestRetryAttemptResult
{
    public int AttemptNumber { get; set; }
    public string? MatchedGroupName { get; set; }
    public bool ShouldRetry { get; set; }
    public double? DelaySeconds { get; set; }
    public string Reason { get; set; } = "";
}
