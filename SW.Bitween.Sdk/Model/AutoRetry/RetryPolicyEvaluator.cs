using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Model;

/// <summary>
/// Evaluates a retry policy against a single failed xchange and decides whether
/// to schedule another attempt.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two independent caps.</strong>  <see cref="RetryBudget.MaxAttemptsPerError"/> is
/// per message and is derived from the caller's <c>attemptIndexForThisMessage</c>, while
/// <see cref="RetryBudget.MaxAttemptsTotal"/> is shared by every message hitting the group and
/// is owned by the injected <see cref="IRetryGroupBudget"/>.  The evaluator itself keeps no
/// counters, so a fresh instance per failure enforces both caps correctly.
/// </para>
/// <para>
/// <strong>Not thread-safe.</strong>  Each task should use its own instance.
/// </para>
/// </remarks>
public class RetryPolicyEvaluator(IRetryPolicy policy, IRetryGroupBudget groupBudget)
{
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
    public async Task<RetryDecision> Evaluate(
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

        // Claimed last so a message already stopped by its own per-message cap doesn't
        // eat a slot out of the shared total.
        if (!await groupBudget.TryConsume(group.Id, budget.MaxAttemptsTotal))
            return RetryDecision.Block(
                $"Group total cap reached ({budget.MaxAttemptsTotal}) for group '{group.Name}'");

        var delay = budget.DelayStrategy.GetDelay(attemptIndexForThisMessage);
        return RetryDecision.Allow(delay, group.Name);
    }

    private RetryGroup? FindMatchingGroup(XchangeResultType resultType, string content)
    {
        foreach (var group in policy.Groups
                     .Where(g => g.Enabled && g.AppliesTo.Contains(resultType))
                     .OrderBy(g => g.Priority))
        {
            var compatibleMatchers = group.Matchers.Where(m => m.Supports(resultType));
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
