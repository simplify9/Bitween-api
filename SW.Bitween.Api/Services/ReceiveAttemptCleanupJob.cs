using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Scheduler;

namespace SW.Bitween;

/// <summary>
/// Deletes <see cref="ReceiveAttempt"/> rows older than
/// <see cref="BitweenOptions.ReceiveAttemptRetentionDays"/>.
/// Scheduled via <see cref="BitweenOptions.ReceiveAttemptCleanupCron"/> (registered by
/// <c>SchedulerSeedService</c>).
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class ReceiveAttemptCleanupJob(BitweenDbContext dbContext, BitweenOptions options) : IScheduledJob
{
    public async Task Execute()
    {
        var cutoff = DateTime.UtcNow.AddDays(-options.ReceiveAttemptRetentionDays);
        await dbContext.Set<ReceiveAttempt>()
            .Where(a => a.StartedOn < cutoff)
            .ExecuteDeleteAsync();
    }
}
