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
    /// Lifts this integration's group budgets that have run out, because it has just succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exhausted total is a statement about a downstream that was failing, and one success says
    /// that is no longer true. Nothing else can say it: an exhausted group schedules no further
    /// retries, so no retry will ever succeed to report the recovery — only ordinary traffic getting
    /// through can. Without this, one bad afternoon stops retrying for good until somebody notices
    /// and resets it by hand.
    /// </para>
    /// <para>
    /// <strong>Only budgets that are actually used up.</strong> A partly-spent total is left alone.
    /// The cap exists to stop a flaky downstream being hammered, and that is precisely a downstream
    /// where some messages succeed and others fail — crediting the total back on every ordinary
    /// success would mean such a subscription never reaches its cap at all.
    /// </para>
    /// <para>
    /// <paramref name="succeededFrom"/> keeps this from erasing a charge it never saw. Bitween runs
    /// several instances, so a failure can claim a slot while this success is still being processed;
    /// deleting that row would hand back a slot already spent and let the group exceed its total.
    /// Only rows whose last attempt predates the success are released.
    /// </para>
    /// <para>
    /// Deleting a row re-arms the exhaustion alert along with the budget, so if the total runs out
    /// again somebody is told again rather than the second outage passing in silence.
    /// </para>
    /// </remarks>
    /// <returns>How many group budgets were released.</returns>
    public async Task<int> ReleaseExhaustedBudgets(DateTime succeededFrom)
    {
        // Cheapest question first, and for almost every success the answer ends it here: a
        // subscription that has never spent a retry has no row, and must not pay for a policy load
        // or a write on the strength of having worked.
        var spent = await dbContext.Set<RetryGroupUsage>().AsNoTracking()
            .Where(u => u.SubscriptionId == subscriptionId)
            .Select(u => new { u.GroupId, u.AttemptsUsed })
            .ToListAsync();
        if (spent.Count == 0) return 0;

        var subscription = await dbContext.Set<Subscription>().AsNoTracking()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        IRetryPolicy policy = subscription?.CustomRetryPolicy ?? (IRetryPolicy)subscription?.RetryPolicy;
        if (policy?.Groups == null) return 0;

        // A group whose total is gone from the policy is left to Update and Delete to clean up, which
        // they already do — releasing it here would be guessing at a cap that no longer exists.
        var exhausted = spent
            .Where(u => policy.Groups.Any(g => g.Id == u.GroupId
                                               && g.Budget is { MaxAttemptsTotal: > 0 }
                                               && u.AttemptsUsed >= g.Budget.MaxAttemptsTotal))
            .Select(u => u.GroupId)
            .ToList();
        if (exhausted.Count == 0) return 0;

        return await dbContext.Set<RetryGroupUsage>()
            .Where(u => u.SubscriptionId == subscriptionId
                        && exhausted.Contains(u.GroupId)
                        && u.LastAttemptOn < succeededFrom)
            .ExecuteDeleteAsync();
    }

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
