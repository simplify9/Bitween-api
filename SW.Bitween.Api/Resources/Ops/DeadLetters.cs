using System.Threading.Tasks;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

[HandlerName("DeadLetters")]
public class DeadLetters(IBusDashboardDataService dashboardDataService) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        return await dashboardDataService.GetDeadLetterSummaryAsync();
    }
}
