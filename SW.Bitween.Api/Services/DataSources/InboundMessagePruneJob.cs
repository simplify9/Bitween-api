using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.DataSources;
using SW.PrimitiveTypes;
using SW.Scheduler;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Forgets dedupe keys once they are older than their data source's window.
///
/// The table only ever grows otherwise - one row per inbound message, for ever. Pruning per data
/// source rather than on one global age matters because the window is a property of the customer's
/// broker: a queue with a seven-day message TTL and one that can be replayed from a dead-letter
/// months later do not want the same answer.
///
/// Forgetting too EARLY is the dangerous direction: a redelivery after the key is gone is
/// processed as a fresh message. Forgetting late merely costs rows.
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class InboundMessagePruneJob(BitweenDbContext dbContext, ILogger<InboundMessagePruneJob> logger) : IScheduledJob
{
    private const int BatchSize = 5_000;

    public async Task Execute()
    {
        var windows = await dbContext.Set<DataSource>().AsNoTracking()
            .Where(d => d.DeduplicationWindowDays > 0)
            .Select(d => new { d.Id, d.DeduplicationWindowDays })
            .ToListAsync();

        var total = 0;

        foreach (var window in windows)
        {
            var cutoff = DateTime.UtcNow.AddDays(-window.DeduplicationWindowDays);

            // Batched: a single unbounded delete on a table this shape can lock for long enough to
            // matter, and there is no urgency about finishing in one pass.
            while (true)
            {
                var batch = await dbContext.Set<InboundMessage>()
                    .Where(m => m.DataSourceId == window.Id && m.SeenOn < cutoff)
                    .Take(BatchSize)
                    .ToListAsync();

                if (batch.Count == 0) break;

                dbContext.RemoveRange(batch);
                await dbContext.SaveChangesAsync();
                total += batch.Count;

                if (batch.Count < BatchSize) break;
            }
        }

        // A data source that has been deleted takes its rows with it via the cascade, so there is
        // nothing orphaned to sweep up here.
        if (total > 0)
            logger.LogInformation("Pruned {Count} dedupe keys past their retention window.", total);
    }
}
