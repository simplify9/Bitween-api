using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Cluster;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.Cluster;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Cluster;

/// <summary>
/// The nodes of this Bitween, as each last described itself (see <see cref="NodeHeartbeat"/>):
/// whether it is still there, whether it runs data sources, which adapter runtimes it has, and the
/// leases and exclusive data sources it holds.
/// </summary>
[HandlerName("nodes")]
public class Nodes(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);

        var now = DateTime.UtcNow;
        var nodes = await dbContext.Set<ClusterNode>().AsNoTracking().OrderBy(n => n.Id).ToListAsync();
        var leases = await dbContext.Set<ClusterLease>().AsNoTracking().ToListAsync();
        var held = await dbContext.Set<DataSource>().AsNoTracking()
            .Where(d => d.OwnedByNode != null)
            .Select(d => new { d.Id, d.Name, d.OwnedByNode })
            .ToListAsync();

        return new
        {
            AnsweredBy = NodeHeartbeat.NodeName,
            GoneAfterSeconds = (int)NodeHeartbeat.GoneAfter.TotalSeconds,
            Nodes = nodes.Select(n => new
            {
                Name = n.Id,
                n.Host,
                n.StartedOn,
                n.LastSeenOn,
                Online = now - n.LastSeenOn <= NodeHeartbeat.GoneAfter,
                n.Version,
                n.DataSources,
                Runtimes = (n.Runtimes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
                Leases = leases.Where(l => l.OwnerNode == n.Id).Select(l => new { Resource = l.Id, l.Term, l.AcquiredOn }),
                // Recorded as "<node> (term N)" by the supervisor that took the source.
                HoldsDataSources = held.Where(d => d.OwnedByNode == n.Id || d.OwnedByNode.StartsWith(n.Id + " "))
                    .Select(d => new { d.Id, d.Name }),
            }),
        };
    }
}
