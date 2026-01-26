using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Update(BitweenDbContext dbContext,IInfolinkCache _BitweenCache, IBroadcast _broadcast) : ICommandHandler<int, CreateWorkGroupModel, object>
{
    public async Task<object> Handle(int key, CreateWorkGroupModel request)
    {
        var workGroup = await dbContext.Set<WorkGroup>().FindAsync(key);
        if (workGroup is null)
            throw new SWValidationException("WORK_GROUP_NOT_FOUND", $"Category with id {key} was not found");
        workGroup.Name = request.Name;
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