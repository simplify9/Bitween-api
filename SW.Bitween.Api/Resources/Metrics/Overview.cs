using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>
/// The dashboard's numbers: traffic by hour against the same hours a week ago, success and
/// end-to-end time, volume by day, the worst and busiest of everything, subscriptions gone quiet,
/// and the broker's backlog over the day. All from the hourly rows; nothing counts exchanges.
/// </summary>
[HandlerName("overview")]
public class Overview(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Dashboard.View);
        return await cache.Cached("overview", Compute);
    }

    async Task<object> Compute()
    {
        var now = DateTime.UtcNow;
        var to = MetricsRecorder.HourOf(now).AddHours(1);

        var hourly = await MetricsQueries.Exchanges(db, to.AddHours(-48), to).GroupBy(s => s.Hour).SumBy();
        var weekAgo = await MetricsQueries.Exchanges(db, to.AddHours(-48).AddDays(-7), to.AddDays(-7)).GroupBy(s => s.Hour).SumBy();
        var last24 = await Total(to.AddHours(-24), to);
        var previous24 = await Total(to.AddHours(-48), to.AddHours(-24));
        var last7 = await Total(to.AddDays(-7), to);
        var daily = await MetricsQueries.Exchanges(db, to.AddDays(-30), to).GroupBy(s => s.Hour.Date).SumBy();

        var subscriptionNames = await db.Set<Subscription>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        var partnerNames = await db.Set<Partner>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

        var bySubscription = await MetricsQueries.Exchanges(db, to.AddHours(-24), to).Where(s => s.SubscriptionId > 0)
            .GroupBy(s => s.SubscriptionId).SumBy();
        var byWeekSubscription = await MetricsQueries.Exchanges(db, to.AddDays(-7), to).Where(s => s.SubscriptionId > 0)
            .GroupBy(s => s.SubscriptionId).SumBy();
        var byPartner = await MetricsQueries.Exchanges(db, to.AddHours(-24), to).Where(s => s.PartnerId > 0)
            .GroupBy(s => s.PartnerId).SumBy();
        var byAdapter = await MetricsQueries.Adapters(db, to.AddHours(-24), to).GroupBy(s => s.AdapterId).SumBy();

        var health = await SubscriptionHealth.ComputeAsync(db, 7, now);
        var backlog = await db.Set<QueueSnapshot>().AsNoTracking().Where(s => s.At >= now.AddHours(-24))
            .OrderBy(s => s.At).ToListAsync();

        return new
        {
            Since = await MetricsQueries.SinceAsync(db),
            Hourly = hourly.OrderBy(h => h.Key).Select(h => new
            {
                Hour = MetricsQueries.Utc(h.Key), h.Total, h.Succeeded, h.BadResponse, h.Failed,
            }),
            WeekAgo = weekAgo.OrderBy(h => h.Key).Select(h => new { Hour = MetricsQueries.Utc(h.Key.AddDays(7)), h.Total }),
            Last24h = Summary(last24),
            Previous24h = Summary(previous24),
            Last7d = Summary(last7),
            Daily = daily.OrderBy(d => d.Key).Select(d => new
            {
                Day = MetricsQueries.Utc(d.Key), d.Total, d.Succeeded, d.BadResponse, d.Failed, Bytes = d.BytesIn + d.BytesOut,
            }),
            TopFailing = bySubscription.Where(s => s.Failed > 0).OrderByDescending(s => s.Failed).Take(5).Select(s => new
            {
                SubscriptionId = s.Key, Name = subscriptionNames.GetValueOrDefault(s.Key), s.Failed, s.Total,
            }),
            BusiestSubscriptions = byWeekSubscription.OrderByDescending(s => s.Total).Take(5).Select(s => new
            {
                SubscriptionId = s.Key, Name = subscriptionNames.GetValueOrDefault(s.Key), s.Total, s.Failed, s.BadResponse,
            }),
            BusiestPartners = byPartner.OrderByDescending(p => p.Total).Take(5).Select(p => new
            {
                PartnerId = p.Key, Name = partnerNames.GetValueOrDefault(p.Key), p.Total, p.Failed,
            }),
            SlowestAdapters = byAdapter.Where(a => a.Calls >= 5).OrderByDescending(a => a.P95Ms).Take(5).Select(a => new
            {
                AdapterId = a.Key, a.Calls, a.P95Ms, a.FailureRate,
            }),
            Silent = health.Where(h => h.Status == "silent").OrderByDescending(h => h.QuietHours).Select(h => new
            {
                h.SubscriptionId, h.Name, h.Type, h.LastExchangeHour, h.QuietHours, h.UsualGapHours,
            }),
            Failing = health.Count(h => h.Status == "failing"),
            Degraded = health.Count(h => h.Status == "degraded"),
            Backlog = backlog.Select(s => new
            {
                At = MetricsQueries.Utc(s.At), s.QueueDepth, s.RetryBacklog, s.DeadLetterBacklog,
            }),
        };
    }

    async Task<ExchangeTotals> Total(DateTime from, DateTime to) =>
        (await MetricsQueries.Exchanges(db, from, to).GroupBy(_ => 1).SumBy()).FirstOrDefault() ?? new ExchangeTotals();

    static object Summary(ExchangeTotals t) => new
    {
        t.Total, t.Succeeded, t.BadResponse, t.Failed, t.Retries, t.ManualRetries, t.SuccessRate, t.P50Ms, t.P95Ms,
        Bytes = t.BytesIn + t.BytesOut,
    };
}
