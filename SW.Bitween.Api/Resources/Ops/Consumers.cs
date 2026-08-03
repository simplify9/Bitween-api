using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

/// <summary>
/// The bus's consumer health, with each row told what it is for. Without the lane and the
/// ids a caller can only see the queue's machine name, and would have to rebuild
/// <see cref="WorkGroup.GetBusMessageName"/> for itself to work out which group a row is.
/// </summary>
public record ConsumerLaneView(
    string Name,
    string MessageName,
    string QueueName,
    string Lane,
    string Title,
    int? WorkGroupId,
    int? InformationTypeId,
    long TotalNodes,
    long ProcessingCount,
    long QueueCount,
    long RetryCount,
    long FailedCount,
    int Priority,
    ushort Prefetch,
    double IncomingRate,
    double ProcessingRate,
    double AckRate,
    bool IsBackpressured,
    AlertSeverity HealthStatus);

[HandlerName("Consumers")]
public class Consumers(IBusDashboardDataService dashboardDataService,
    LaneResolver laneResolver,
    BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View, Model.Permissions.Dashboard.View);

        var health = await dashboardDataService.GetConsumerHealthAsync();
        await laneResolver.Prepare();

        return health.Select(c =>
        {
            var identity = laneResolver.Resolve(c.Name, c.MessageName);
            return new ConsumerLaneView(
                c.Name,
                c.MessageName,
                c.QueueName,
                identity.Lane.ToString(),
                identity.Title,
                identity.WorkGroupId,
                identity.InformationTypeId,
                c.TotalNodes,
                c.ProcessingCount,
                c.QueueCount,
                c.RetryCount,
                c.FailedCount,
                c.Priority,
                c.Prefetch,
                c.IncomingRate,
                c.ProcessingRate,
                c.AckRate,
                c.IsBackpressured,
                c.HealthStatus);
        }).ToArray();
    }
}
