using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
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
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.RetryPolicies.Delete);

        var inUse = await _dbContext.Set<Subscription>()
            .AnyAsync(s => s.RetryPolicyId == key);
        if (inUse)
            throw new SWException("Cannot delete a retry policy that is assigned to one or more subscriptions.");

        // Same reason as Update: the policy's groups are about to stop existing, so clear their
        // usage rows rather than strand them.
        var policy = await _dbContext.FindAsync<RetryPolicy>(key);
        var groupIds = policy.Groups.Select(g => g.Id).ToList();

        // One change, one commit — same reasoning as Update: a half-done delete leaves rows keyed
        // by groups that no longer exist anywhere, which nothing can then reach.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        await _dbContext.DeleteByKeyAsync<RetryPolicy>(key);

        if (groupIds.Count > 0)
        {
            await _dbContext.Set<RetryGroupUsage>()
                .Where(u => groupIds.Contains(u.GroupId))
                .ExecuteDeleteAsync();

            await _dbContext.Set<RetryAlertOverride>()
                .Where(o => groupIds.Contains(o.GroupId))
                .ExecuteDeleteAsync();
        }

        await transaction.CommitAsync();
        return null;
    }
}
