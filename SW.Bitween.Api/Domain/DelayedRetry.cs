using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;
// Id should be the same for xchangeId when retry happens the record is deleted
public class DelayedRetry : BaseEntity<string>
{
    public DateTime On { get; set; }

    /// <summary>
    /// <see cref="On"/> for a held retry: one kept because its group's total budget is spent, never
    /// due until <see cref="ReleaseHeldAsync"/> makes it due. A date rather than a column, so holding
    /// needed no schema change; the retry job never reaches it.
    /// </summary>
    public static readonly DateTime HeldOn = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    public bool Held => On >= HeldOn;

    /// <summary>
    /// Makes the held retries of these subscriptions due now: their budget has been lifted, by a
    /// success or by hand.
    /// </summary>
    /// <returns>How many were released.</returns>
    public static Task<int> ReleaseHeldAsync(BitweenDbContext dbContext, IReadOnlyCollection<int> subscriptionIds)
    {
        var now = DateTime.UtcNow;
        return dbContext.Set<DelayedRetry>()
            .Where(r => r.On >= HeldOn && dbContext.Set<Xchange>()
                .Any(x => x.Id == r.Id && x.SubscriptionId != null && subscriptionIds.Contains(x.SubscriptionId.Value)))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.On, now));
    }

    /// <summary>
    /// How many times running this retry has failed. A failure is rescheduled, not dropped - a
    /// database blip used to delete the retry for good - up to <see cref="RetryJob.MaxRunFailures"/>.
    /// </summary>
    public int RunFailures { get; set; }
}
