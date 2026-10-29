using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ApiGateways;

/// <summary>
/// The calls to this gateway turned away, and why: an unknown or missing key, a partner not
/// attached, the gateway switched off, no subscription behind it, too many calls. Kept in memory on
/// the node that answers, since it started (see <see cref="GatewayActivity"/>); another node keeps
/// its own.
/// </summary>
[HandlerName("rejections")]
public class Rejections(BitweenDbContext dbContext, RequestContext requestContext, GatewayActivity activity)
    : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ApiGateways.View);
        var urlName = await dbContext.Set<ApiGateway>().AsNoTracking().Where(g => g.Id == key)
            .Select(g => g.UrlName).SingleOrDefaultAsync() ?? throw new SWNotFoundException(key.ToString());

        var (counts, recent) = activity.RefusalsOf(urlName);

        // Every node's, kept past restarts: the hourly metrics count each refusal under the reason.
        var scopeKey = urlName.Trim('/').ToLowerInvariant();
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var lastSevenDays = await dbContext.Set<Domain.Metrics.ErrorStats>().AsNoTracking()
            .Where(e => e.Scope == Domain.Metrics.ErrorStats.GatewayScope && e.ScopeKey == scopeKey && e.Hour >= weekAgo)
            .GroupBy(e => e.Sample)
            .Select(g => new { Reason = g.Key, Count = g.Sum(x => (long)x.Count) })
            .ToDictionaryAsync(x => x.Reason ?? "", x => x.Count);

        return new
        {
            Node = Services.Cluster.NodeHeartbeat.NodeName,
            activity.Since,
            Counts = counts,
            LastSevenDays = lastSevenDays,
            Recent = recent.Select(r => new { r.On, r.Reason, r.Status, r.Address }),
        };
    }
}
