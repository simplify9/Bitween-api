using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>Every adapter's use over the window: calls, failures by kind, timings, calls by day, last call.</summary>
[HandlerName("adapters")]
public class Adapters(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache)
    : IQueryHandler<MetricsWindowRequest, object>
{
    public async Task<object> Handle(MetricsWindowRequest request)
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Subscriptions.View);
        var days = MetricsQueries.Days(request?.Days);
        return await cache.Cached($"adapters.{days}", () => Compute(days));
    }

    async Task<object> Compute(int days)
    {
        var to = MetricsRecorder.HourOf(DateTime.UtcNow).AddHours(1);
        var from = to.AddDays(-days);
        var totals = await MetricsQueries.Adapters(db, from, to).GroupBy(s => s.AdapterId).SumBy();
        var last = await MetricsQueries.Adapters(db, to.AddDays(-30), to)
            .GroupBy(s => s.AdapterId).Select(g => new { g.Key, Last = g.Max(x => x.Hour) })
            .ToDictionaryAsync(x => x.Key, x => MetricsQueries.Utc(x.Last));
        var dailyRows = await MetricsQueries.Adapters(db, from, to)
            .GroupBy(s => new { s.AdapterId, Day = s.Hour.Date })
            .Select(g => new { g.Key.AdapterId, g.Key.Day, Calls = g.Sum(x => (long)x.Calls), Failures = g.Sum(x => (long)x.Failures) })
            .ToListAsync();
        var firstDay = from.Date;

        return new
        {
            Since = await MetricsQueries.AdapterSinceAsync(db),
            Days = days,
            Adapters = totals.Select(t => new
            {
                Id = t.Key, t.Calls, t.Failures, t.FailureRate, t.Timeouts, t.LimitStops, t.Crashes, t.P50Ms, t.P95Ms,
                MaxMs = t.DurationMaxMs,
                LastCalledOn = last.TryGetValue(t.Key, out var l) ? l : (DateTime?)null,
                Daily = Enumerable.Range(0, days).Select(i =>
                {
                    var day = firstDay.AddDays(i);
                    var r = dailyRows.Where(d => d.AdapterId == t.Key && d.Day.Date == day).ToList();
                    return new { Day = MetricsQueries.Utc(day), Calls = r.Sum(x => x.Calls), Failures = r.Sum(x => x.Failures) };
                }),
            }),
            // Adapters with no calls in the window but some in the last 30 days, for "last called".
            Quiet = last.Where(l => totals.All(t => t.Key != l.Key)).Select(l => new { Id = l.Key, LastCalledOn = l.Value }),
        };
    }
}
