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
    public const int MaxRunFailures = 5;

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
                    // Whatever the failed run left staged goes first — saving it would commit the very
                    // changes that failing was meant to prevent.
                    dbContext.ChangeTracker.Clear();

                    await AfterRunFailure(dbContext, delayedRetry, ex, logger);
                }
            }
        }
    }

    /// <summary>
    /// What happens to a retry whose run threw. Every row leaves the pass one way or another, which
    /// is what stops the loop from meeting the same row again and turning the drain into a spin:
    /// moved later, so a passing failure — the database for a moment — doesn't lose the retry, or
    /// dropped once it has kept failing. A run whose save did commit has already removed its row,
    /// so neither statement touches it.
    /// </summary>
    public static async Task AfterRunFailure(BitweenDbContext dbContext, DelayedRetry delayedRetry, Exception ex,
        ILogger logger)
    {
        var failures = delayedRetry.RunFailures + 1;
        if (failures >= MaxRunFailures)
        {
            logger.LogError(ex,
                "The scheduled retry of xchange {XchangeId} failed {Failures} times; clearing its schedule.",
                delayedRetry.Id, failures);
            await dbContext.Set<DelayedRetry>()
                .Where(r => r.Id == delayedRetry.Id)
                .ExecuteDeleteAsync();
            return;
        }

        var next = DateTime.UtcNow.AddMinutes(Math.Pow(2, failures));
        logger.LogWarning(ex,
            "The scheduled retry of xchange {XchangeId} did not complete; trying again at {Next}.",
            delayedRetry.Id, next);
        await dbContext.Set<DelayedRetry>()
            .Where(r => r.Id == delayedRetry.Id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.On, next)
                .SetProperty(r => r.RunFailures, failures));
    }
}
