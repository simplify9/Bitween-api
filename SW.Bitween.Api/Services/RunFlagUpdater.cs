using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween
{
    public class RunFlagUpdater(BitweenDbContext dbContext, BitweenOptions options, ILogger<RunFlagUpdater> logger)
    {
        /// <summary>
        /// Claims the subscription for one run. A single conditional UPDATE, so of two runners only
        /// one changes the row, on every provider — the raw SQL this replaced read the flag with
        /// SELECT ... FOR UPDATE outside a transaction on MySQL, which released the lock before the
        /// UPDATE and let both through.
        /// </summary>
        /// <returns>False when another run holds it.</returns>
        public async Task<bool> MarkAsRunning(int id)
        {
            var now = DateTime.UtcNow;
            var staleBefore = now.AddMinutes(-options.StaleRunAfterMinutes);

            // A flag with no start time was set before RunningSince existed, and a flag older than
            // the limit belongs to a run that died with its process. Either can be taken over.
            var abandoned = await dbContext.Set<Subscription>().AsNoTracking()
                .AnyAsync(s => s.Id == id && s.IsRunning && (s.RunningSince == null || s.RunningSince < staleBefore));

            var claimed = await dbContext.Set<Subscription>()
                .Where(s => s.Id == id &&
                            (!s.IsRunning || s.RunningSince == null || s.RunningSince < staleBefore))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.IsRunning, true)
                    .SetProperty(s => s.RunningSince, now));

            if (claimed == 1 && abandoned)
                logger.LogWarning(
                    "Subscription {SubscriptionId} was still marked running from an earlier run that never finished; taking it over.",
                    id);

            return claimed == 1;
        }

        public async Task MarkAsIdle(int id)
        {
            await dbContext.Set<Subscription>()
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.IsRunning, false)
                    .SetProperty(s => s.RunningSince, (DateTime?)null));
        }

        /// <summary>
        /// No longer queried. Kept because it is part of the model: removing it would change every
        /// provider's snapshot, and the PostgreSQL one with a migration.
        /// </summary>
        public class RunningResult
        {
            public bool IsRunning { get; set; }
        }
    }
}