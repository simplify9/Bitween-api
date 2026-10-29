using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Failed deliveries in a row, as a subscription's page shows them.
/// </summary>
/// <remarks>
/// A subscription set to auto-pause counts its failures on its results, and a success no longer
/// writes the subscription row back to 0 (finding 4i of docs/design/processing-performance.md), so
/// its latest result says where the run stands. The row is still written on each failure and set
/// back to 0 on resuming, so 0 there is 0. Receivers and aggregations count their runs on the row,
/// as does anything whose latest result has no count.
/// </remarks>
static class SubscriptionFailures
{
    public static async Task<int> CountAsync(BitweenDbContext dbContext, Subscription subscription)
    {
        // 0 on the row is a run started again by resuming it, or no failure since counting began.
        if (subscription.AutoPauseAfterFailures == null || subscription.ConsecutiveFailures == 0)
            return subscription.ConsecutiveFailures;
        var latest = await dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(r => r.SubscriptionId == subscription.Id)
            .OrderByDescending(r => r.FinishedOn)
            .Select(r => r.ConsecutiveFailures)
            .FirstOrDefaultAsync();
        return latest ?? subscription.ConsecutiveFailures;
    }
}
