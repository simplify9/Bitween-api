using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>Every partner's traffic over the window, when it was last seen, and when each of its keys was last used.</summary>
[HandlerName("partners")]
public class Partners(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache)
    : IQueryHandler<MetricsWindowRequest, object>
{
    public async Task<object> Handle(MetricsWindowRequest request)
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Partners.View);
        var days = MetricsQueries.Days(request?.Days);
        return await cache.Cached($"partners.{days}", () => Compute(days));
    }

    async Task<object> Compute(int days)
    {
        var to = MetricsRecorder.HourOf(DateTime.UtcNow).AddHours(1);
        var from = to.AddDays(-days);
        var totals = (await MetricsQueries.Exchanges(db, from, to).Where(s => s.PartnerId > 0).GroupBy(s => s.PartnerId).SumBy())
            .ToDictionary(t => t.Key);
        var last = await MetricsQueries.Exchanges(db, to.AddDays(-30), to).Where(s => s.PartnerId > 0 && s.Total > 0)
            .GroupBy(s => s.PartnerId).Select(g => new { g.Key, Last = g.Max(x => x.Hour) })
            .ToDictionaryAsync(x => x.Key, x => MetricsQueries.Utc(x.Last));
        var keys = await db.Set<ApiKeyUse>().AsNoTracking()
            .GroupBy(k => k.PartnerId).Select(g => new { g.Key, Last = g.Max(k => k.LastUsedOn) })
            .ToDictionaryAsync(x => x.Key, x => MetricsQueries.Utc(x.Last));
        var partners = await db.Set<Partner>().AsNoTracking().Where(p => p.Id != Partner.SystemId)
            .Select(p => new { p.Id, p.Name }).ToListAsync();

        return new
        {
            Since = await MetricsQueries.SinceAsync(db),
            Days = days,
            Partners = partners.Select(p =>
            {
                var t = totals.GetValueOrDefault(p.Id);
                return new
                {
                    PartnerId = p.Id, p.Name,
                    Exchanges = t?.Total ?? 0, Failed = t?.Failed ?? 0, BadResponse = t?.BadResponse ?? 0,
                    SuccessRate = t?.SuccessRate, Bytes = (t?.BytesIn ?? 0) + (t?.BytesOut ?? 0),
                    LastExchangeHour = last.TryGetValue(p.Id, out var l) ? l : (DateTime?)null,
                    KeyLastUsedOn = keys.TryGetValue(p.Id, out var k) ? k : (DateTime?)null,
                };
            }).OrderByDescending(p => p.Exchanges),
        };
    }
}
