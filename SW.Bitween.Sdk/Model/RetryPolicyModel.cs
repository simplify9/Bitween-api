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
