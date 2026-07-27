using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Create : ICommandHandler<RetryPolicyCreate, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(RetryPolicyCreate model)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.RetryPolicies.Create);

        var entity = new RetryPolicy
        {
            Name = model.Name,
            Groups = model.Groups ?? []
        };
        _dbContext.Add(entity);
        await _dbContext.SaveChangesAsync();
        return entity.Id;
    }
}
