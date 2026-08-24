using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache _BitweenCache, IBroadcast _broadcast) : ICommandHandler<int, CreateWorkGroupModel, object>
{
    private readonly RequestContext _requestContext = requestContext;

    public async Task<object> Handle(int key, CreateWorkGroupModel request)
    {
        await _requestContext.EnsurePermission(dbContext, Model.Permissions.WorkGroups.Edit);

        var workGroup = await dbContext.Set<WorkGroup>().FindAsync(key);
        if (workGroup is null)
            throw new SWValidationException("WORK_GROUP_NOT_FOUND", $"Category with id {key} was not found");

        // Update binds the same CreateWorkGroupModel type as Create, so Create's
        // Validate (IValidator<CreateWorkGroupModel>) already runs for this request too —
        // CqApiController resolves validators by the request's concrete type.
        workGroup.Name = request.Name;
        workGroup.BusMessageName = request.BusMessageName;
        workGroup.Options = new WorkGroupOptions
        {
            RabbitMqOptions = new ConsumerSettings
            {
                Prefetch = request.Options?.RabbitMqOptions?.Prefetch,
                Priority = request.Options?.RabbitMqOptions?.Priority
            }
        };
        
        await dbContext.SaveChangesAsync();
        
        await _BitweenCache.BroadcastRevoke();
        await _broadcast.RefreshConsumers();
        
        return null;
    }
}