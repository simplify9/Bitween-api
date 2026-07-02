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
            var xchange = await dbContext.FindAsync<Xchange>(delayedRetry.Id);
            if (xchange == null)
            {
                dbContext.Remove(delayedRetry);
                continue;
            }

            // Resolve subscription before fetching the file to avoid a cloud round-trip for orphan records.
            var subscription = await dbContext.Set<Subscription>()
                .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);

            if (subscription == null)
            {
                dbContext.Remove(delayedRetry);
                continue;
            }

            var inputFileData = await xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
            var inputFile = new XchangeFile(inputFileData, xchange.InputName);

            await xchangeService.CreateXchange(subscription, xchange, inputFile,
                groupAttemptCounts: delayedRetry.GroupAttemptCounts);
            dbContext.Remove(delayedRetry);
        }

        await dbContext.SaveChangesAsync();
    }
}
