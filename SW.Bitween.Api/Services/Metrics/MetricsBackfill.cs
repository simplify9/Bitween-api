using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Metrics;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// Folds the exchanges from before metrics were recorded into the hourly rows, once, so the charts
/// have history from the day of the upgrade and not only from then on.
/// </summary>
/// <remarks>
/// Built to be gentle on a busy database: one hour at a time, newest first (so recent days appear
/// soonest), over the indexed <c>Xchange.StartedOn</c>, streamed rather than loaded, with a pause
/// between hours. Each hour is written with a marker row in one save; if two nodes take the same
/// hour, the key lets one of them write it and the other moves on, so nothing is counted twice.
/// It starts below the hour live recording began in, so the two never overlap.
/// </remarks>
public class MetricsBackfill(IServiceScopeFactory scopes, BitweenOptions options, ILogger<MetricsBackfill> logger)
    : BackgroundService
{
    /// <summary>Subscription and partner of the row holding the hour live recording began.</summary>
    public const int CutoffMark = -2;

    public static TimeSpan StartAfter { get; set; } = TimeSpan.FromMinutes(2);
    public static TimeSpan PauseBetweenHours { get; set; } = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.MetricsBackfillDays <= 0) return;
        try
        {
            await Task.Delay(StartAfter, stoppingToken);
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping; what is done stays done, the rest is picked up next start.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Folding exchange history into the metrics stopped; it carries on at the next start.");
        }
    }

    /// <summary>Does the whole backfill now. Public for tests.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        var cutoff = await CutoffAsync(cancellation);
        var oldest = cutoff.AddDays(-options.MetricsBackfillDays);
        for (var hour = cutoff.AddHours(-1); hour >= oldest; hour = hour.AddHours(-1))
        {
            cancellation.ThrowIfCancellationRequested();
            if (await FoldHourAsync(hour, cancellation) && PauseBetweenHours > TimeSpan.Zero)
                await Task.Delay(PauseBetweenHours, cancellation);
        }
    }

    /// <summary>
    /// The hour live recording began in, written the first time any node asks and read ever after:
    /// later restarts must not move it, or hours would be counted by both.
    /// </summary>
    async Task<DateTime> CutoffAsync(CancellationToken cancellation)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var mark = await Mark(db).Select(s => (DateTime?)s.Hour).FirstOrDefaultAsync(cancellation);
        if (mark != null) return DateTime.SpecifyKind(mark.Value, DateTimeKind.Utc);

        db.Add(new ExchangeStats
        {
            Hour = MetricsRecorder.HourOf(DateTime.UtcNow), Node = ExchangeStats.HistoryNode,
            SubscriptionId = CutoffMark, PartnerId = CutoffMark,
        });
        try
        {
            await db.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateException)
        {
            // Another node wrote it first; theirs counts.
        }

        await using var again = scopes.CreateAsyncScope();
        var hour = await Mark(again.ServiceProvider.GetRequiredService<BitweenDbContext>())
            .Select(s => s.Hour).FirstAsync(cancellation);
        return DateTime.SpecifyKind(hour, DateTimeKind.Utc);
    }

    static IQueryable<ExchangeStats> Mark(BitweenDbContext db) =>
        db.Set<ExchangeStats>().AsNoTracking()
            .Where(s => s.Node == ExchangeStats.HistoryNode && s.SubscriptionId == CutoffMark && s.PartnerId == CutoffMark)
            .OrderBy(s => s.Hour);

    /// <summary>Counts one hour of history, unless it is done already. True when it did the work.</summary>
    async Task<bool> FoldHourAsync(DateTime hour, CancellationToken cancellation)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        if (await db.Set<ExchangeStats>().AnyAsync(s => s.Hour == hour && s.Node == ExchangeStats.HistoryNode &&
                                                       s.SubscriptionId == ExchangeStats.HourDone, cancellation))
            return false;

        var end = hour.AddHours(1);
        var rows = new Dictionary<(int, int), ExchangeStats>();
        var finished =
            from x in db.Set<Xchange>().AsNoTracking()
            where x.StartedOn >= hour && x.StartedOn < end
            join r in db.Set<XchangeResult>().AsNoTracking() on x.Id equals r.Id
            select new
            {
                x.SubscriptionId, x.PartnerId, x.StartedOn, x.InputSize, IsRetry = x.RetryFor != null, x.ManualRetry,
                r.Success, r.ResponseBad, r.FinishedOn, r.OutputSize, r.ResponseSize,
            };
        await foreach (var e in finished.AsAsyncEnumerable().WithCancellation(cancellation))
        {
            var key = (e.SubscriptionId ?? ExchangeStats.None, e.PartnerId ?? ExchangeStats.None);
            if (!rows.TryGetValue(key, out var row))
                rows[key] = row = new ExchangeStats
                {
                    Hour = hour, Node = ExchangeStats.HistoryNode, SubscriptionId = key.Item1, PartnerId = key.Item2,
                };
            row.Total++;
            if (!e.Success) row.Failed++;
            else if (e.ResponseBad) row.BadResponse++;
            else row.Succeeded++;
            if (e.IsRetry) row.Retries++;
            if (e.ManualRetry) row.ManualRetries++;
            row.BytesIn += e.InputSize;
            row.BytesOut += e.OutputSize + e.ResponseSize;
            row.AddDuration(Math.Max(0, (long)(e.FinishedOn - e.StartedOn).TotalMilliseconds));
        }

        db.AddRange(rows.Values);
        db.Add(new ExchangeStats
        {
            Hour = hour, Node = ExchangeStats.HistoryNode, SubscriptionId = ExchangeStats.HourDone, PartnerId = ExchangeStats.HourDone,
        });
        try
        {
            await db.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateException)
        {
            // Another node folded this hour at the same time and saved first.
        }
        return true;
    }
}
