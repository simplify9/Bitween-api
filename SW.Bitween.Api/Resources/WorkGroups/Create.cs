using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

public class Create : ICommandHandler<CreateWorkGroupModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

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
        _dbContext.Add(workgroup);
        await _dbContext.SaveChangesAsync();
        return new
        {
            workgroup.Id
        };
    }
}