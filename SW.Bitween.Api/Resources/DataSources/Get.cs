using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DataSources;

public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.View);

        var dataSource = await dbContext.Set<DataSource>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == key);

        if (dataSource == null)
            throw new SWNotFoundException($"DataSource with id '{key}' was not found");

        return new DataSourceRow
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

            // Masked, always. This is the only endpoint that returns connection settings, so it is
            // the only place a broker password could leave the process.
            Properties = Secrets.Mask(dataSource.Properties, dataSource.SecretProperties),
            SecretProperties = dataSource.SecretProperties,

            LastKnownState = dataSource.LastKnownState,
            LastHeartbeatOn = dataSource.LastHeartbeatOn,
            LastException = dataSource.LastException,
            ConsecutiveFailures = dataSource.ConsecutiveFailures,
            OwnedByNode = dataSource.OwnedByNode,

            GatewayCount = await dbContext.Set<BusGateway>()
                .CountAsync(gateway => gateway.DataSourceId == key),
            SubscriptionCount = await dbContext.Set<Subscription>()
                .CountAsync(subscription => subscription.DataSourceId == key)
        };
    }
}
