using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DataSources;

public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        // Lookup is id/name pairs, which the bus gateway picker needs in order to offer a data
        // source at all; the full list carries connection detail, so that is what View covers.
        if (!lookup)
            await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.View);

        var query = from dataSource in dbContext.Set<DataSource>()
            select new DataSourceRow
            {
                Id = dataSource.Id,
                Name = dataSource.Name,
                AdapterId = dataSource.AdapterId,
                Kind = dataSource.Kind.ToString(),
                Placement = dataSource.Placement.ToString(),
                Inactive = dataSource.Inactive,
                DeduplicationWindowDays = dataSource.DeduplicationWindowDays,
                SoftMemoryLimitMb = dataSource.SoftMemoryLimitMb,
                CpuPercentLimit = dataSource.CpuPercentLimit,
                CpuLimitSamples = dataSource.CpuLimitSamples,
                HardMemoryLimitMb = dataSource.HardMemoryLimitMb,
                LastKnownState = dataSource.LastKnownState,
                LastHeartbeatOn = dataSource.LastHeartbeatOn,
                LastException = dataSource.LastException,
                ConsecutiveFailures = dataSource.ConsecutiveFailures,
                OwnedByNode = dataSource.OwnedByNode,

                // A correlated count, so the "used by" column costs one subquery per row rather
                // than the whole BusGateway table over the wire.
                GatewayCount = dbContext.Set<BusGateway>()
                    .Count(gateway => gateway.DataSourceId == dataSource.Id),
                SubscriptionCount = dbContext.Set<Subscription>()
                    .Count(subscription => subscription.DataSourceId == dataSource.Id)
            };

        query = query.AsNoTracking();

        // Properties are deliberately absent from the list: it is a table of connections, and
        // sending every credential — even masked — to render a row is not worth the exposure.

        if (lookup)
            return await query.Search(searchyRequest.Conditions)
                .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);

        return new SearchyResponse<DataSourceRow>
        {
            TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
            Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts,
                searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
        };
    }
}
