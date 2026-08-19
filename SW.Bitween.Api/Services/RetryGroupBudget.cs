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
    public async Task<RetryBudgetClaim> TryConsume(Guid groupId, int maxAttemptsTotal)
    {
        // A group configured to allow no retries at all has no budget to exhaust, so it never
        // alerts — otherwise every single failure under it would raise one.
        if (maxAttemptsTotal <= 0) return RetryBudgetClaim.Denied;

        if (await TryIncrement(dbContext, groupId, maxAttemptsTotal)) return RetryBudgetClaim.Allowed;

        // Nothing was updated: either the ceiling is reached, or this integration and group have
        // never failed before and so have no row yet.
        var exists = await dbContext.Set<RetryGroupUsage>()
            .AnyAsync(u => u.SubscriptionId == subscriptionId && u.GroupId == groupId);
        if (exists) return await ClaimExhaustionAlert(groupId, maxAttemptsTotal);

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
            return RetryBudgetClaim.Allowed;
        }
        catch (DbUpdateException)
        {
            // The winner of the insert race already holds a row, so this is the ordinary
            // increment path again — including the case where their row is already full.
            return await TryIncrement(dbContext, groupId, maxAttemptsTotal)
                ? RetryBudgetClaim.Allowed
                : await ClaimExhaustionAlert(groupId, maxAttemptsTotal);
        }
    }

    /// <summary>
    /// Hands back every group total this integration has spent, because it has just succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spent total is a statement about a downstream that was failing, and one success says that is
    /// no longer true. Nothing else can say it: an exhausted group schedules no further retries, so
    /// the only evidence left that the downstream recovered is an ordinary message getting through.
    /// Without this, one bad afternoon stops retrying for good until somebody notices and resets it
    /// by hand.
    /// </para>
    /// <para>
    /// Every group is cleared rather than only the exhausted one. The caps are per group, but the
    /// downstream they were all failing against is shared, and a success is evidence about that
    /// downstream.
    /// </para>
    /// <para>
    /// Deleting the rows re-arms the exhaustion alert along with the budget, so if the total runs out
    /// again somebody is told again rather than the second outage passing in silence.
    /// </para>
    /// </remarks>
    public Task ClearAfterSuccess() =>
        dbContext.Set<RetryGroupUsage>()
            .Where(u => u.SubscriptionId == subscriptionId)
            .ExecuteDeleteAsync();

    /// <summary>
    /// Takes responsibility for alerting that this integration's budget for the group is spent.
    /// </summary>
    /// <remarks>
    /// One conditional UPDATE for the same reason the increment is one: several instances can
    /// discover the empty budget at the same moment, and a read-then-write would let each of them
    /// decide it was the first. Exactly one caller updates a row, so exactly one alert is raised —
    /// and because Reset deletes the row outright, clearing a budget re-arms the alert with it.
    /// </remarks>
    private async Task<RetryBudgetClaim> ClaimExhaustionAlert(Guid groupId, int maxAttemptsTotal)
    {
        var claimed = await dbContext.Set<RetryGroupUsage>()
            .Where(u => u.SubscriptionId == subscriptionId
                        && u.GroupId == groupId
                        && u.AttemptsUsed >= maxAttemptsTotal
                        && u.ExhaustedNotifiedOn == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ExhaustedNotifiedOn, _ => DateTime.UtcNow)) > 0;

        return claimed ? RetryBudgetClaim.DeniedAndJustExhausted : RetryBudgetClaim.Denied;
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
