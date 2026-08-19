using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween;

/// <summary>
/// Polls for due <see cref="DelayedRetry"/> records and re-submits the failed Xchanges.
/// Scheduled via <see cref="BitweenOptions.RetryJobCron"/> (registered by <c>SchedulerSeedService</c>).
/// </summary>
/// <remarks>
/// Works through every retry that is already due rather than a hundred a minute, and commits one row
/// at a time. Committing the batch in one go meant a single row that could not be carried out
/// discarded the work of all the others and left their schedules in place, so the same batch came
/// back a minute later and failed the same way — no retry would ever have run again.
/// </remarks>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class RetryJob(BitweenDbContext dbContext, XchangeService xchangeService, ILogger<RetryJob> logger)
    : IScheduledJob
{
    private const int BatchSize = 100;

    public async Task Execute()
    {
        // Fixed before the first batch: a retry scheduled while this run is working belongs to the next
        // tick, otherwise a fast-failing subscription could keep this run going indefinitely.
        var due = DateTime.UtcNow;

        while (true)
        {
            var ready = await dbContext.Set<DelayedRetry>()
                .Where(r => r.On <= due)
                .OrderBy(r => r.On)
                .Take(BatchSize)
                .ToListAsync();

            if (ready.Count == 0) return;

            foreach (var delayedRetry in ready)
            {
                try
                {
                    await xchangeService.ExecuteDelayedRetry(delayedRetry);
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Not "dropped": SaveChangesAsync commits before it publishes, so a failure in the
                    // publish leaves the replacement exchange committed and only its announcement
                    // missing. Saying the retry was dropped would send whoever reads this looking for
                    // an exchange that does exist.
                    logger.LogError(ex,
                        "The scheduled retry of xchange {XchangeId} did not complete; clearing its "
                        + "schedule so the queue keeps draining.", delayedRetry.Id);

                    // Whatever the failed run left staged goes first — saving it would commit the very
                    // changes that failing was meant to prevent.
                    dbContext.ChangeTracker.Clear();

                    // Every row leaves the queue one way or another, which is what stops the loop above
                    // from meeting the same row again and turning the drain into a spin.
                    await dbContext.Set<DelayedRetry>()
                        .Where(r => r.Id == delayedRetry.Id)
                        .ExecuteDeleteAsync();
                }
            }
        }
    }
}
