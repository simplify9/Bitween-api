using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// <see cref="IRetryGroupBudget"/> backed by the <c>RetryGroupUsage</c> table and scoped to one
/// integration, so a policy template shared by many integrations gives each its own total
/// instead of letting one noisy integration spend everyone's budget.
/// </summary>
public class RetryGroupBudget(
    BitweenDbContext dbContext,
    IServiceProvider serviceProvider,
    int subscriptionId) : IRetryGroupBudget
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The claim is a single conditional <c>UPDATE</c>, so the check and the increment happen as
    /// one database operation. Bitween runs several instances, and a read-then-write would let two
    /// simultaneous failures both observe the last free slot and both retry, exceeding the cap.
    /// </para>
    /// <para>
    /// Because it commits on its own rather than with the caller's <c>SaveChangesAsync</c>, a slot
    /// can be charged for a retry that is never scheduled if that later save fails. That errs
    /// toward retrying less than the cap allows, and <c>RetryPolicies/resetusage</c> can return it.
    /// The alternative — an explicit transaction spanning the caller's save — would publish this
    /// xchange's bus events before the commit, since BitweenDbContext dispatches domain events
    /// inside SaveChangesAsync.
    /// </para>
    /// </remarks>
    public async Task<bool> TryConsume(Guid groupId, int maxAttemptsTotal)
    {
        if (maxAttemptsTotal <= 0) return false;

        if (await TryIncrement(dbContext, groupId, maxAttemptsTotal)) return true;

        // Nothing was updated: either the ceiling is reached, or this integration and group have
        // never failed before and so have no row yet.
        var exists = await dbContext.Set<RetryGroupUsage>()
            .AnyAsync(u => u.SubscriptionId == subscriptionId && u.GroupId == groupId);
        if (exists) return false;

        // Create that first row on its own context so it commits independently of whatever the
        // caller still has pending. Losing this race is harmless: the primary key rejects the
        // duplicate and the conditional increment is then applied to the winner's row.
        using var scope = serviceProvider.CreateScope();
        var isolated = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        isolated.Add(new RetryGroupUsage
        {
            SubscriptionId = subscriptionId,
            GroupId = groupId,
            AttemptsUsed = 1,
            LastAttemptOn = DateTime.UtcNow
        });

        try
        {
            await isolated.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return await TryIncrement(dbContext, groupId, maxAttemptsTotal);
        }
    }

    private async Task<bool> TryIncrement(BitweenDbContext db, Guid groupId, int maxAttemptsTotal) =>
        await db.Set<RetryGroupUsage>()
            .Where(u => u.SubscriptionId == subscriptionId
                        && u.GroupId == groupId
                        && u.AttemptsUsed < maxAttemptsTotal)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AttemptsUsed, u => u.AttemptsUsed + 1)
                .SetProperty(u => u.LastAttemptOn, _ => DateTime.UtcNow)) > 0;
}
