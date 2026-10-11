using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Metrics;
using SW.Scheduler;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// Deletes hourly metrics older than <see cref="BitweenOptions.MetricsRetentionDays"/> and broker
/// snapshots older than a month. Each delete is a range on the leading column of the table's key.
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class MetricsPruneJob(BitweenDbContext db, BitweenOptions options) : IScheduledJob
{
    public static readonly TimeSpan SnapshotsKeptFor = TimeSpan.FromDays(31);

    public async Task Execute()
    {
        var cutoff = MetricsRecorder.HourOf(DateTime.UtcNow).AddDays(-Math.Max(1, options.MetricsRetentionDays));
        // The marker of where recorded history begins stays: without it the backfill would start over.
        await db.Set<ExchangeStats>()
            .Where(s => s.Hour < cutoff && s.SubscriptionId != MetricsBackfill.CutoffMark)
            .ExecuteDeleteAsync();
        await db.Set<AdapterStats>().Where(s => s.Hour < cutoff).ExecuteDeleteAsync();
        await db.Set<ErrorStats>().Where(s => s.Hour < cutoff).ExecuteDeleteAsync();
        var snapshots = DateTime.UtcNow - SnapshotsKeptFor;
        await db.Set<QueueSnapshot>().Where(s => s.At < snapshots).ExecuteDeleteAsync();
    }
}
