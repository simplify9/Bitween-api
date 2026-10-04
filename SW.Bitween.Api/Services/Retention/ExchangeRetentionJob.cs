using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Scheduler;

namespace SW.Bitween;

/// <summary>
/// Removes exchanges older than <see cref="BitweenOptions.ExchangeRetentionDays"/>, archiving each one
/// first while <see cref="BitweenOptions.ArchiveExchanges"/> is on. Scheduled by
/// <see cref="BitweenOptions.ExchangeRetentionCron"/>; does nothing while retention is 0.
/// </summary>
/// <remarks>
/// Exchange files are left alone: the bucket's lifecycle rule deletes those on its own clock. The job
/// works oldest first in small batches, each committed on its own, so the first run on a large table
/// can be stopped at any point and the next run carries on from there.
/// </remarks>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class ExchangeRetentionJob(BitweenDbContext dbContext, BitweenOptions options, ExchangeArchive archive,
    ILogger<ExchangeRetentionJob> logger) : IScheduledJob
{
    internal const int BatchSize = 200;

    public async Task Execute()
    {
        var days = options.ExchangeRetentionDays;
        if (days <= 0) return;

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = 0;

        while (true)
        {
            var batch = await ExchangeRetention.DueBefore(dbContext, cutoff)
                .AsNoTracking().Take(BatchSize).ToListAsync();
            if (batch.Count == 0) break;

            var done = options.ArchiveExchanges ? await archive.WriteAsync(batch) : batch;
            if (done.Count > 0)
            {
                await Delete(done.Select(x => x.Id).ToList());
                removed += done.Count;
            }

            // Something couldn't be archived. It's kept, and so is everything after it, until a later
            // run gets it into the archive — retrying here would only fail the same way.
            if (done.Count < batch.Count)
            {
                logger.LogError(
                    "Exchange retention stopped after removing {Removed} exchange(s): one could not be archived.",
                    removed);
                return;
            }
        }

        if (removed > 0)
            logger.LogInformation("Exchange retention removed {Removed} exchange(s) that started before {Cutoff:u}.",
                removed, cutoff);
    }

    /// <summary>
    /// Results, promoted properties, aggregation links and deliveries go with the exchange through their
    /// cascading foreign keys. Notifications have none, so they go first.
    /// </summary>
    private async Task Delete(System.Collections.Generic.List<string> ids)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.Set<XchangeNotification>().Where(n => ids.Contains(n.XchangeId)).ExecuteDeleteAsync();
        await dbContext.Set<Xchange>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}

/// <summary>Which exchanges retention removes, shared by the job and the settings page's preview.</summary>
public static class ExchangeRetention
{
    /// <summary>
    /// Exchanges that started before <paramref name="cutoff"/>, oldest first. One still waiting for a
    /// scheduled retry stays: removing it would silently cancel the retry. So does one a newer retry
    /// still points at, until that retry goes too, so a retry never loses the attempt it came from.
    /// </summary>
    public static IQueryable<Xchange> DueBefore(BitweenDbContext dbContext, DateTime cutoff) =>
        dbContext.Set<Xchange>()
            .Where(x => x.StartedOn < cutoff
                        && !dbContext.Set<DelayedRetry>().Any(d => d.Id == x.Id)
                        && !dbContext.Set<Xchange>().Any(retry => retry.RetryFor == x.Id))
            .OrderBy(x => x.StartedOn);
}
