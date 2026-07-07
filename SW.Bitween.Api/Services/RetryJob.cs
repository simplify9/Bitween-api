using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Scheduler;

namespace SW.Bitween;

/// <summary>
/// Polls for due <see cref="DelayedRetry"/> records and re-submits the failed Xchanges.
/// Scheduled via <see cref="BitweenOptions.RetryJobCron"/> (registered by <c>SchedulerSeedService</c>).
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class RetryJob(BitweenDbContext dbContext, XchangeService xchangeService) : IScheduledJob
{
    private const int BatchSize = 100;

    public async Task Execute()
    {
        var ready = await dbContext.Set<DelayedRetry>()
            .Where(r => r.On <= DateTime.UtcNow)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var delayedRetry in ready)
        {
            await xchangeService.ExecuteDelayedRetry(delayedRetry);
        }

        await dbContext.SaveChangesAsync();
    }
}
