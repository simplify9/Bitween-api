using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Update : ICommandHandler<int, RetryPolicyUpdate, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RetryPolicyUpdate model)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        var entity = await _dbContext.FindAsync<RetryPolicy>(key);
        entity.Name = model.Name;
        entity.Groups = model.Groups ?? [];
        await _dbContext.SaveChangesAsync();
        return null;
    }
}
