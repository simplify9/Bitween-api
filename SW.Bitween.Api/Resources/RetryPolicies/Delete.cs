using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Delete : IDeleteHandler<int, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Delete(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        var inUse = await _dbContext.Set<Subscription>()
            .AnyAsync(s => s.RetryPolicyId == key);
        if (inUse)
            throw new SWException("Cannot delete a retry policy that is assigned to one or more subscriptions.");

        await _dbContext.DeleteByKeyAsync<RetryPolicy>(key);
        return null;
    }
}
