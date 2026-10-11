using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Metrics;
using SW.Bus.RabbitMqExtensions;
using SW.Scheduler;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// Records the broker's queue totals every few minutes, for the backlog trend. One call to the
/// RabbitMQ management API and one small row; nothing at all when the management API isn't set up.
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class QueueSnapshotJob(BitweenDbContext db, BitweenOptions options, IServiceProvider services) : IScheduledJob
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    public async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(options.RabbitMqManagementUrl)) return;
        var dashboard = services.GetService<IBusDashboardDataService>();
        if (dashboard == null) return;

        var summary = await dashboard.GetSummaryAsync();
        var now = DateTime.UtcNow;
        // Cut to the interval: if more than one node samples at once, the key keeps one of them.
        var at = new DateTime(now.Ticks - now.Ticks % Every.Ticks, DateTimeKind.Utc);
        if (await db.Set<QueueSnapshot>().AnyAsync(s => s.At == at)) return;
        db.Add(new QueueSnapshot
        {
            At = at,
            QueueDepth = summary.TotalQueueDepth,
            RetryBacklog = summary.TotalRetryBacklog,
            DeadLetterBacklog = summary.TotalDeadLetterBacklog,
            IncomingRate = summary.TotalIncomingRate,
            AckRate = summary.TotalAckRate,
            Consumers = summary.TotalConsumers,
            UnhealthyConsumers = summary.UnhealthyConsumers,
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another node wrote this interval's snapshot first. One is all that's wanted.
        }
    }
}
