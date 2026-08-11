using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class RetryPolicyCreate
{
    public required string Name { get; set; }
    public List<RetryGroup> Groups { get; set; } = [];
}

public class RetryPolicyUpdate : RetryPolicyCreate { }

public class RetryPolicyRow
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int GroupCount { get; set; }
}

/// <summary>
/// How much of a group's <see cref="RetryBudget.MaxAttemptsTotal"/> one integration has spent.
/// The total is tracked per integration, so a policy shared by several yields one row each.
/// </summary>
public class RetryGroupUsageRow
{
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>Null when the group has since been renamed away or removed from the policy.</summary>
    public string GroupName { get; set; }

    public int AttemptsUsed { get; set; }
    public int MaxAttemptsTotal { get; set; }

    /// <summary>True when the budget is spent and this integration will get no further retries.</summary>
    public bool Exhausted { get; set; }

    public DateTime LastAttemptOn { get; set; }
}

/// <summary>Empty request body — the policy is identified by the route key.</summary>
public class RetryPolicyUsageRequest
{
}

/// <summary>
/// Clears spent budget so a group starts retrying again. Omit both fields to reset every
/// integration and group of the policy.
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
