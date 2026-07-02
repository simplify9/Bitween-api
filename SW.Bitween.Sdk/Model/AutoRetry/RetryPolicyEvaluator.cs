using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Model;

/// <summary>
/// Evaluates a retry policy against a single failed xchange and decides whether
/// to schedule another attempt.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Stateful per processing window.</strong>  The evaluator accumulates
/// per-group attempt totals in <c>_groupAttemptCounts</c>.  For a single in-process
/// window (e.g. a running <c>RetryJob</c> batch) one instance handles all messages.
/// </para>
/// <para>
/// <strong>Cross-invocation persistence.</strong>  When a retry is scheduled the
/// current counts are saved to <see cref="GetGroupAttemptCounts"/> and stored on
/// the <c>DelayedRetry</c> entity (and on the new <c>Xchange</c> so that a later
/// failure of the retry itself can pick up where the budgets left off).  On the next
/// evaluation call <see cref="RestoreGroupAttemptCounts"/> before <see cref="Evaluate"/>.
/// </para>
/// <para>
/// <strong>Not thread-safe.</strong>  Each goroutine/task should use its own instance.
/// </para>
/// </remarks>
public class RetryPolicyEvaluator(IRetryPolicy policy)
{
    private readonly Dictionary<Guid, int> _groupAttemptCounts = new();

    /// <summary>
    /// Restores previously persisted group-level attempt counts, allowing budgets
    /// to continue from where they left off across separate process invocations.
    /// </summary>
    /// <param name="counts">
    /// The dictionary returned by <see cref="GetGroupAttemptCounts"/> from a prior evaluation.
    /// String keys are parsed back to <see cref="Guid"/> — invalid entries are silently ignored.
    /// </param>
    public void RestoreGroupAttemptCounts(Dictionary<string, int> counts)
    {
        foreach (var kv in counts)
            if (Guid.TryParse(kv.Key, out var guid))
                _groupAttemptCounts[guid] = kv.Value;
    }

    /// <summary>
    /// Returns the current group-level attempt counts as a string-keyed dictionary
    /// suitable for JSON serialisation and storage on <c>DelayedRetry</c> / <c>Xchange</c>.
    /// </summary>
    public Dictionary<string, int> GetGroupAttemptCounts() =>
        _groupAttemptCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    /// <summary>
    /// Evaluates the policy and returns a retry decision for the failed xchange.
    /// </summary>
    /// <param name="resultType">
    /// <see cref="XchangeResultType.Error"/> or <see cref="XchangeResultType.BadResult"/>.
    /// Passing <see cref="XchangeResultType.Success"/> throws <see cref="InvalidOperationException"/>.
    /// </param>
    /// <param name="content">
    /// Raw failure content: exception stack-trace text for <c>Error</c>,
    /// or the JSON response string for <c>BadResult</c>.
    /// </param>
    /// <param name="attemptIndexForThisMessage">
    /// How many times this specific message has already been attempted (0-based).
    /// Used to enforce <see cref="RetryBudget.MaxAttemptsPerError"/>.
    /// </param>
    /// <returns>
    /// A <see cref="RetryDecision"/> indicating whether to retry and, if so, how long to wait.
    /// </returns>
    public RetryDecision Evaluate(
        XchangeResultType resultType,
        string content,
        int attemptIndexForThisMessage)
    {
        if (resultType == XchangeResultType.Success)
            throw new InvalidOperationException("Success results must never be evaluated for retry.");

        var group = FindMatchingGroup(resultType, content);

        if (group is null)
            return RetryDecision.Block("No matching group (default block)");

        if (group.Action == RetryAction.Block)
            return RetryDecision.Block($"Group '{group.Name}' explicitly blocks this error");

        var budget = group.Budget!;

        if (attemptIndexForThisMessage >= budget.MaxAttemptsPerError)
            return RetryDecision.Block(
                $"Per-message cap reached ({budget.MaxAttemptsPerError}) in group '{group.Name}'");

        var totalUsed = _groupAttemptCounts.GetValueOrDefault(group.Id, 0);
        if (totalUsed >= budget.MaxAttemptsTotal)
            return RetryDecision.Block(
                $"Group total cap reached ({budget.MaxAttemptsTotal}) for group '{group.Name}'");

        _groupAttemptCounts[group.Id] = totalUsed + 1;

        var delay = budget.DelayStrategy.GetDelay(attemptIndexForThisMessage);
        return RetryDecision.Allow(delay, group.Name);
    }

    private RetryGroup? FindMatchingGroup(XchangeResultType resultType, string content)
    {
        foreach (var group in policy.Groups
                     .Where(g => g.Enabled && g.AppliesTo.Contains(resultType))
                     .OrderBy(g => g.Priority))
        {
            var compatibleMatchers = group.Matchers.Where(m => m.ResultType == resultType);
            if (compatibleMatchers.Any(m => m.IsMatch(content)))
                return group;
        }
        return null;
    }
}

/// <summary>
/// The outcome of a single <see cref="RetryPolicyEvaluator.Evaluate"/> call.
/// </summary>
public class RetryDecision
{
    /// <summary><c>true</c> when a retry should be scheduled; <c>false</c> to drop the message.</summary>
    public bool ShouldRetry { get; private init; }

    /// <summary>How long to wait before the retry attempt. Meaningful only when <see cref="ShouldRetry"/> is <c>true</c>.</summary>
    public TimeSpan Delay { get; private init; }

    /// <summary>Human-readable explanation of the decision, useful for audit/debug logs.</summary>
    public string Reason { get; private init; } = "";

    /// <summary>Name of the <see cref="RetryGroup"/> that matched, or <c>null</c> when blocked.</summary>
    public string? MatchedGroupName { get; private init; }

    /// <summary>Creates an Allow decision — a retry will be scheduled after <paramref name="delay"/>.</summary>
    public static RetryDecision Allow(TimeSpan delay, string groupName) => new()
    {
        ShouldRetry = true,
        Delay = delay,
        MatchedGroupName = groupName,
        Reason = $"Allowed by group '{groupName}'"
    };

    /// <summary>Creates a Block decision — no retry will be scheduled.</summary>
    public static RetryDecision Block(string reason) => new()
    {
        ShouldRetry = false,
        Reason = reason
    };
}
