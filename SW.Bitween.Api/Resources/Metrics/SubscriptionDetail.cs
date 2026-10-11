using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>
/// One subscription's numbers for its page: its health, traffic by hour over two days and by day
/// over the window, where failures came from, retries, nodes, and its most common errors.
/// </summary>
[HandlerName("subscription")]
public class SubscriptionDetail(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache)
    : IQueryHandler<SubscriptionMetricsRequest, object>
{
    public async Task<object> Handle(SubscriptionMetricsRequest request)
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Subscriptions.View);
        var days = MetricsQueries.Days(request?.Days);
        var id = request?.Id ?? 0;
        return await cache.Cached($"subscription.{id}.{days}", () => Compute(id, days));
    }

    async Task<object> Compute(int id, int days)
    {
        var now = DateTime.UtcNow;
        var to = MetricsRecorder.HourOf(now).AddHours(1);
        var from = to.AddDays(-days);
        var rows = MetricsQueries.Exchanges(db, from, to).Where(s => s.SubscriptionId == id);

        var total = (await rows.GroupBy(_ => 1).SumBy()).FirstOrDefault() ?? new ExchangeTotals<int>();
        var hourly = await MetricsQueries.Exchanges(db, to.AddHours(-48), to).Where(s => s.SubscriptionId == id)
            .GroupBy(s => s.Hour).SumBy();
        var daily = await rows.GroupBy(s => s.Hour.Date).SumBy();
        var nodes = await rows.GroupBy(s => s.Node).SumBy();
        var health = (await SubscriptionHealth.ComputeAsync(db, days, now)).FirstOrDefault(h => h.SubscriptionId == id);

        var key = id.ToString();
        var errorRows = db.Set<ErrorStats>().AsNoTracking()
            .Where(e => e.Scope == ErrorStats.SubscriptionScope && e.ScopeKey == key && e.Hour >= from && e.Hour < to);
        var errors = await errorRows.GroupBy(e => e.Fingerprint)
            .Select(g => new { Fingerprint = g.Key, Count = g.Sum(x => (long)x.Count), LastSeenOn = g.Max(x => x.LastSeenOn) })
            .OrderByDescending(e => e.Count).Take(10).ToListAsync();
        var fingerprints = errors.Select(e => e.Fingerprint).ToList();
        var samples = (await errorRows.Where(e => fingerprints.Contains(e.Fingerprint))
                .Select(e => new { e.Fingerprint, e.Sample, e.LastSeenOn }).ToListAsync())
            .GroupBy(e => e.Fingerprint).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.LastSeenOn).First().Sample);

        return new
        {
            Id = id,
            Days = days,
            Since = await MetricsQueries.SinceAsync(db),
            Status = health?.Status,
            health?.LastExchangeHour,
            health?.QuietHours,
            health?.UsualGapHours,
            Total = new
            {
                total.Total, total.Succeeded, total.BadResponse, total.Failed, total.FailedAtMapper, total.FailedAtHandler,
                total.Retries, total.ManualRetries, total.SuccessRate, total.P50Ms, total.P95Ms, Bytes = total.BytesIn + total.BytesOut,
            },
            Hourly = hourly.OrderBy(h => h.Key).Select(h => new { Hour = MetricsQueries.Utc(h.Key), h.Total, h.Failed, h.BadResponse }),
            Daily = daily.OrderBy(d => d.Key).Select(d => new { Day = MetricsQueries.Utc(d.Key), d.Total, d.Failed, d.P95Ms }),
            Nodes = nodes.OrderByDescending(n => n.Total).Select(n => new { Node = n.Key, n.Total, n.Failed }),
            Errors = errors.Select(e => new
            {
                e.Fingerprint, e.Count, LastSeenOn = MetricsQueries.Utc(e.LastSeenOn), Sample = samples.GetValueOrDefault(e.Fingerprint),
            }),
        };
    }
}
