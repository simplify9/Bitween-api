using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>
/// A named family of errors or bad-results that share one retry budget.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AppliesTo</strong> gates which <see cref="XchangeResultType"/> values this group
/// handles. The evaluator skips groups whose <c>AppliesTo</c> list does not contain the
/// current result type, so an <c>ExceptionTypeMatcher</c> can never accidentally fire on a
/// JSON payload and vice-versa.
/// </para>
/// <para>
/// <strong>Matcher logic</strong>: OR — the group fires as soon as any single matcher returns
/// <c>true</c>. Matchers incompatible with the current result type are silently skipped.
/// </para>
/// <para>
/// <strong>Priority</strong>: lower numbers are evaluated first. Leave gaps (10, 20, 30 …)
/// so new groups can be inserted without renumbering.
/// </para>
/// </remarks>
public class RetryGroup
{
    /// <summary>Stable identifier used to track per-group attempt counts across retries.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Human-readable label. Required; used in <see cref="RetryDecision.MatchedGroupName"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Evaluation order. Lower number = higher priority.</summary>
    public int Priority { get; init; }

    /// <summary>When <c>false</c> the group is skipped entirely during evaluation.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Which result types this group handles.
    /// Common values: <c>["Error"]</c>, <c>["BadResult"]</c>, or <c>["Error","BadResult"]</c>.
    /// </summary>
    public List<XchangeResultType> AppliesTo { get; init; } = [];

    /// <summary>OR-logic matchers. The group fires when any one of these returns <c>true</c>.</summary>
    public List<Matcher> Matchers { get; init; } = [];

    /// <summary>
    /// Whether to allow or hard-block retries when this group matches.
    /// Defaults to <see cref="RetryAction.Allow"/>.
    /// </summary>
    public RetryAction Action { get; init; } = RetryAction.Allow;

    /// <summary>
    /// Retry limits and backoff strategy. Must be non-null when
    /// <see cref="Action"/> is <see cref="RetryAction.Allow"/>; ignored when <c>Block</c>.
    /// </summary>
    public RetryBudget? Budget { get; init; }

    /// <summary>Optional free-text notes visible in the management UI.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Whether this group defines its own destination for budget-exhausted alerts, suppresses the
    /// policy's, or defers to it. Defaults to <see cref="RetryAlertMode.Inherit"/> so groups saved
    /// before alerts existed keep using the policy's setting.
    /// </summary>
    public RetryAlertMode AlertMode { get; init; } = RetryAlertMode.Inherit;

    /// <summary>
    /// Adapter that delivers this group's alert. Required when <see cref="AlertMode"/> is
    /// <see cref="RetryAlertMode.Send"/>, ignored otherwise.
    /// </summary>
    public string? AlertHandlerId { get; init; }

    /// <summary>That adapter's own settings — api key, recipients, subject.</summary>
    public Dictionary<string, string>? AlertHandlerProperties { get; init; }
}

/// <summary>
/// Retry limits and backoff schedule for a single <see cref="RetryGroup"/>.
/// </summary>
public class RetryBudget
{
    /// <summary>
    /// Maximum number of retry attempts for a single failing message within this group.
    /// Prevents one flapping integration from consuming all retries indefinitely.
    /// </summary>
    public int MaxAttemptsPerError { get; init; }

    /// <summary>
    /// Hard ceiling on the total number of group-level retries across all messages, counted per
    /// subscription so one shared policy does not let a single noisy subscription spend everyone's
    /// allowance. Prevents a burst of failures from hammering the downstream. It is not a rate over a
    /// rolling window: the count only falls once it has been reached — the subscription's next success
    /// then lifts it — or when somebody resets it by hand.
    /// </summary>
    public int MaxAttemptsTotal { get; init; }

    /// <summary>Calculates the wait duration before each successive retry attempt.</summary>
    public required DelayStrategy DelayStrategy { get; init; }
}
