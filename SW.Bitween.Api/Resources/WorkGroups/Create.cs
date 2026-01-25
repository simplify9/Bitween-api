using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model; 
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Create(BitweenDbContext dbContext, RequestContext requestContext,IInfolinkCache _BitweenCache, IBroadcast _broadcast)
    : ICommandHandler<CreateWorkGroupModel, object>
{
    private readonly RequestContext _requestContext = requestContext;

    public async Task<object> Handle(CreateWorkGroupModel request)
    {
        var workgroup = new WorkGroup()
        {
            Name = request.Name,
            BusMessageName =  request.BusMessageName,
            Options = new WorkGroupOptions()
            {
                RabbitMqOptions = new ConsumerSettings
                {
                    Prefetch = request.Options?.RabbitMqOptions?.Prefetch,
                    Priority = request.Options?.RabbitMqOptions?.Priority
                }
            }
        };
        dbContext.Add(workgroup);
        await dbContext.SaveChangesAsync();
        _BitweenCache.BroadcastRevoke();
        await _broadcast.RefreshConsumers();
        return new
        {
            workgroup.Id
        };
    }
}