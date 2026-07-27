using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

[HandlerName("Queues")]
public class Queues(IBusDashboardDataService dashboardDataService,
    BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View, Model.Permissions.Dashboard.View);

        return await dashboardDataService.GetQueueDetailsAsync();
    }
}
