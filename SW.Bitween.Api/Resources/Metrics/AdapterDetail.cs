using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>
/// One adapter's use in detail: by version, by node, by role and runtime, by day; its most common
/// errors; and the subscriptions that use it with what they moved through it.
/// </summary>
[HandlerName("adapter")]
public class AdapterDetail(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache)
    : IQueryHandler<AdapterMetricsRequest, object>
{
    public async Task<object> Handle(AdapterMetricsRequest request)
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Subscriptions.View);
        if (string.IsNullOrWhiteSpace(request?.Id)) throw new SWValidationException("Id", "Say which adapter.");
        var id = request.Id.Trim().ToLowerInvariant();
        var days = MetricsQueries.Days(request.Days);
        return await cache.Cached($"adapter.{id}.{days}", () => Compute(id, days));
    }

    async Task<object> Compute(string id, int days)
    {
        var to = MetricsRecorder.HourOf(DateTime.UtcNow).AddHours(1);
        var from = to.AddDays(-days);
        var rows = MetricsQueries.Adapters(db, from, to).Where(s => s.AdapterId == id);

        var total = (await rows.GroupBy(_ => 1).SumBy()).FirstOrDefault() ?? new AdapterTotals<int>();
        var versions = await rows.GroupBy(s => s.Version).SumBy();
        var nodes = await rows.GroupBy(s => s.Node).SumBy();
        var roles = await rows.GroupBy(s => new { s.Role, s.Runtime }).SumBy();
        var daily = await rows.GroupBy(s => s.Hour.Date).SumBy();

        var errorRows = db.Set<ErrorStats>().AsNoTracking()
            .Where(e => e.Scope == ErrorStats.AdapterScope && e.ScopeKey == id && e.Hour >= from && e.Hour < to);
        var errors = await errorRows.GroupBy(e => e.Fingerprint)
            .Select(g => new { Fingerprint = g.Key, Count = g.Sum(x => (long)x.Count), LastSeenOn = g.Max(x => x.LastSeenOn) })
            .OrderByDescending(e => e.Count).Take(10).ToListAsync();
        var fingerprints = errors.Select(e => e.Fingerprint).ToList();
        var samples = (await errorRows.Where(e => fingerprints.Contains(e.Fingerprint))
                .Select(e => new { e.Fingerprint, e.Sample, e.LastSeenOn }).ToListAsync())
            .GroupBy(e => e.Fingerprint).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.LastSeenOn).First().Sample);

        // The subscriptions configured with it now, and what went through them in the window.
        var users = await db.Set<Subscription>().AsNoTracking()
            .Where(s => s.HandlerId.ToLower() == id || s.MapperId.ToLower() == id ||
                        s.ReceiverId.ToLower() == id || s.ValidatorId.ToLower() == id)
            .Select(s => new { s.Id, s.Name, s.Type, s.HandlerVersion, s.MapperVersion })
            .ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        var through = (await MetricsQueries.Exchanges(db, from, to).Where(s => userIds.Contains(s.SubscriptionId))
            .GroupBy(s => s.SubscriptionId).SumBy()).ToDictionary(t => t.Key);

        return new
        {
            Id = id,
            Days = days,
            Since = await MetricsQueries.AdapterSinceAsync(db),
            Total = Shape(total),
            Versions = versions.OrderByDescending(v => v.Calls).Select(v => new { Version = v.Key, Totals = Shape(v) }),
            Nodes = nodes.OrderByDescending(n => n.Calls).Select(n => new { Node = n.Key, Totals = Shape(n) }),
            Roles = roles.Select(r => new { r.Key.Role, r.Key.Runtime, Totals = Shape(r) }),
            Daily = daily.OrderBy(d => d.Key).Select(d => new { Day = MetricsQueries.Utc(d.Key), d.Calls, d.Failures, d.P95Ms }),
            Errors = errors.Select(e => new
            {
                e.Fingerprint, e.Count, LastSeenOn = MetricsQueries.Utc(e.LastSeenOn), Sample = samples.GetValueOrDefault(e.Fingerprint),
            }),
            Subscriptions = users.Select(u =>
            {
                var t = through.GetValueOrDefault(u.Id);
                return new
                {
                    SubscriptionId = u.Id, u.Name, Type = u.Type.ToString(),
                    Exchanges = t?.Total ?? 0, Failed = t?.Failed ?? 0, Bytes = (t?.BytesIn ?? 0) + (t?.BytesOut ?? 0),
                };
            }).OrderByDescending(u => u.Exchanges),
        };
    }

    static object Shape(AdapterTotals t) => new
    {
        t.Calls, t.Failures, t.FailureRate, t.Timeouts, t.LimitStops, t.Crashes, t.P50Ms, t.P95Ms, MaxMs = t.DurationMaxMs,
    };
}
