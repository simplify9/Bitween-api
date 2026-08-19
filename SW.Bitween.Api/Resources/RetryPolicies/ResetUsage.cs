using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Clears spent group budget, letting an exhausted group retry again. A budget also clears itself
/// when the integration next succeeds, so this is for putting one back before that happens.
/// </summary>
[HandlerName("resetusage")]
public class ResetUsage : ICommandHandler<int, RetryPolicyResetUsage, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public ResetUsage(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RetryPolicyResetUsage request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        var policy = await _dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        // Scope the reset to this policy's own integrations and groups, so a policy id in the
        // route can never clear a counter belonging to a different policy.
        var subscriptionIds = await _dbContext.Set<Subscription>()
            .Where(s => s.RetryPolicyId == key)
            .Select(s => s.Id)
            .ToListAsync();

        var groupIds = policy.Groups.Select(g => g.Id).ToList();

        var query = _dbContext.Set<RetryGroupUsage>()
            .Where(u => subscriptionIds.Contains(u.SubscriptionId) && groupIds.Contains(u.GroupId));

        if (request.SubscriptionId.HasValue)
            query = query.Where(u => u.SubscriptionId == request.SubscriptionId.Value);

        if (request.GroupId.HasValue)
            query = query.Where(u => u.GroupId == request.GroupId.Value);

        await query.ExecuteDeleteAsync();
        return null;
    }
}
