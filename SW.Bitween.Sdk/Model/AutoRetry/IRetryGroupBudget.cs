using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Model;

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
    /// <returns>
    /// <c>true</c> when a slot was claimed; <c>false</c> when the total is already spent
    /// and no further retry may be scheduled for this group.
    /// </returns>
    Task<bool> TryConsume(Guid groupId, int maxAttemptsTotal);
}

/// <summary>
/// In-memory <see cref="IRetryGroupBudget"/> for the policy dry-run endpoint, where nothing
/// should be persisted and the budget spans only the simulated run.
/// </summary>
public class InMemoryRetryGroupBudget : IRetryGroupBudget
{
    private readonly Dictionary<Guid, int> _used = new();

    /// <inheritdoc/>
    public Task<bool> TryConsume(Guid groupId, int maxAttemptsTotal)
    {
        var used = _used.GetValueOrDefault(groupId, 0);
        if (used >= maxAttemptsTotal) return Task.FromResult(false);

        _used[groupId] = used + 1;
        return Task.FromResult(true);
    }
}
