using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.Cluster;
using SW.Bitween.Services.Cluster;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>
/// What each node has done in the last day - exchanges, adapter calls and how they failed - and
/// which resident adapter instances it is running now, with what they use. A node whose failures
/// stand out from the rest is the one to look at.
/// </summary>
[HandlerName("nodes")]
public class Nodes(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Settings.View, Model.Permissions.Monitoring.View);
        return await cache.Cached("nodes", Compute);
    }

    async Task<object> Compute()
    {
        var now = DateTime.UtcNow;
        var to = MetricsRecorder.HourOf(now).AddHours(1);
        var from = to.AddHours(-24);
        var exchanges = (await MetricsQueries.Exchanges(db, from, to).GroupBy(s => s.Node).SumBy()).ToDictionary(t => t.Key);
        var adapters = (await MetricsQueries.Adapters(db, from, to).GroupBy(s => s.Node).SumBy()).ToDictionary(t => t.Key);
        var nodes = await db.Set<ClusterNode>().AsNoTracking().OrderBy(n => n.Id).ToListAsync();

        return new
        {
            Nodes = nodes.Select(n =>
            {
                var e = exchanges.GetValueOrDefault(n.Id.Length <= 100 ? n.Id : n.Id[..100]);
                var a = adapters.GetValueOrDefault(n.Id.Length <= 100 ? n.Id : n.Id[..100]);
                return new
                {
                    n.Id,
                    n.Host,
                    Alive = now - n.LastSeenOn < NodeHeartbeat.GoneAfter,
                    LastSeenOn = MetricsQueries.Utc(n.LastSeenOn),
                    Exchanges = e?.Total ?? 0,
                    Failed = e?.Failed ?? 0,
                    SuccessRate = e?.SuccessRate,
                    P95Ms = e?.P95Ms,
                    AdapterCalls = a?.Calls ?? 0,
                    AdapterFailures = a?.Failures ?? 0,
                    Timeouts = a?.Timeouts ?? 0,
                    LimitStops = a?.LimitStops ?? 0,
                    Crashes = a?.Crashes ?? 0,
                    Resident = Parse(n.ResidentHealth),
                };
            }),
        };
    }

    static JToken Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JArray();
        try
        {
            return JToken.Parse(json);
        }
        catch
        {
            return new JArray();
        }
    }
}
