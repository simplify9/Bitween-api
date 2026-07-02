using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>
/// Common contract for both named <see cref="RetryPolicy"/> templates and inline
/// <see cref="CustomRetryPolicy"/> objects stored directly on a subscription.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>Ordered set of groups evaluated against a failed xchange.</summary>
    List<RetryGroup> Groups { get; }
}

/// <summary>
/// An inline retry policy defined directly on a subscription rather than referencing a
/// shared named template. Serialised as JSONB in the <c>subscription</c> table.
/// </summary>
public class CustomRetryPolicy : IRetryPolicy
{
    /// <inheritdoc/>
    public List<RetryGroup> Groups { get; set; } = [];
}

/// <summary>Outcome type of a completed xchange execution.</summary>
public enum XchangeResultType
{
    /// <summary>Handler returned a successful response. Never retried.</summary>
    Success,

    /// <summary>An unhandled exception was thrown. Content is a stack-trace string.</summary>
    Error,

    /// <summary>Handler completed but the response payload failed business validation.</summary>
    BadResult,
}

/// <summary>
/// Intended scope of a retry policy (informational; not enforced by the evaluator).
/// </summary>
public enum PolicyScope
{
    Global,
    Integration,
}

/// <summary>What the evaluator should do when a group matches a failure.</summary>
public enum RetryAction
{
    /// <summary>Schedule a retry according to the group's <see cref="RetryBudget"/>.</summary>
    Allow,

    /// <summary>Hard-block this error — no retry even if a budget would otherwise allow it.</summary>
    Block,
}
