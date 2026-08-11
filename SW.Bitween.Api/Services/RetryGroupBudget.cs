using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// <see cref="IRetryGroupBudget"/> backed by the <c>RetryGroupUsage</c> table and scoped to one
/// integration, so a policy template shared by many integrations gives each its own total
/// instead of letting one noisy integration spend everyone's budget.
/// </summary>
public class RetryGroupBudget(BitweenDbContext dbContext, int subscriptionId) : IRetryGroupBudget
{
    /// <inheritdoc/>
    /// <remarks>
    /// The increment is left for the caller's <c>SaveChangesAsync</c> so it commits in the same
    /// transaction as the <c>DelayedRetry</c> row it authorises — a scheduled retry and its
    /// spent slot can never disagree.  Two failures of the same group evaluated concurrently can
    /// each read the same count and overshoot the cap by the number of simultaneous failures.
    /// </remarks>
    public async Task<bool> TryConsume(Guid groupId, int maxAttemptsTotal)
    {
        var usage = await dbContext.Set<RetryGroupUsage>()
            .FirstOrDefaultAsync(u => u.SubscriptionId == subscriptionId && u.GroupId == groupId);

        if ((usage?.AttemptsUsed ?? 0) >= maxAttemptsTotal) return false;

        if (usage == null)
            dbContext.Add(new RetryGroupUsage
            {
                SubscriptionId = subscriptionId,
                GroupId = groupId,
                AttemptsUsed = 1,
                LastAttemptOn = DateTime.UtcNow
            });
        else
        {
            usage.AttemptsUsed++;
            usage.LastAttemptOn = DateTime.UtcNow;
        }

        return true;
    }
}
