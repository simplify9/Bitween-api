using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Model;

/// <summary>
/// The outcome of asking a <see cref="RetryGroup"/>'s shared total budget for one attempt.
/// </summary>
/// <param name="Granted">
/// <c>true</c> when a slot was claimed and a retry may be scheduled.
/// </param>
/// <param name="JustExhausted">
/// <c>true</c> only for the single caller that first found the budget spent, so an
/// exhaustion alert is raised once rather than on every failure that follows.
/// Always <c>false</c> when <paramref name="Granted"/> is <c>true</c>.
/// </param>
public readonly record struct RetryBudgetClaim(bool Granted, bool JustExhausted)
{
    /// <summary>A slot was claimed.</summary>
    public static RetryBudgetClaim Allowed => new(true, false);

    /// <summary>No slot available, and someone else has already taken responsibility for alerting.</summary>
    public static RetryBudgetClaim Denied => new(false, false);

    /// <summary>No slot available, and this caller owns the alert for it.</summary>
    public static RetryBudgetClaim DeniedAndJustExhausted => new(false, true);
}

/// <summary>
/// Tracks how much of a <see cref="RetryGroup"/>'s <see cref="RetryBudget.MaxAttemptsTotal"/>
/// has already been spent.
/// </summary>
/// <remarks>
/// The total is a ceiling shared by every message that hits the group, so the count cannot
/// live on the message being evaluated — it needs a store that outlives a single xchange.
/// The implementation decides the scope: the production one keys by integration + group and
/// persists, while <see cref="InMemoryRetryGroupBudget"/> counts only for one dry-run.
/// </remarks>
public interface IRetryGroupBudget
{
    /// <summary>Claims one attempt from the group's total budget.</summary>
    Task<RetryBudgetClaim> TryConsume(Guid groupId, int maxAttemptsTotal);
}

/// <summary>
/// In-memory <see cref="IRetryGroupBudget"/> for the policy dry-run endpoint, where nothing
/// should be persisted and the budget spans only the simulated run.
/// </summary>
public class InMemoryRetryGroupBudget : IRetryGroupBudget
{
    private readonly Dictionary<Guid, int> _used = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Never reports <see cref="RetryBudgetClaim.JustExhausted"/>: simulating a policy must not
    /// send anyone an alert.
    /// </remarks>
    public Task<RetryBudgetClaim> TryConsume(Guid groupId, int maxAttemptsTotal)
    {
        var used = _used.GetValueOrDefault(groupId, 0);
        if (used >= maxAttemptsTotal) return Task.FromResult(RetryBudgetClaim.Denied);

        _used[groupId] = used + 1;
        return Task.FromResult(RetryBudgetClaim.Allowed);
    }
}
