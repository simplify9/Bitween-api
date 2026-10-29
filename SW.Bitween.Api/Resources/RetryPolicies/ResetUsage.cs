using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Clears spent group budget, letting an exhausted group retry again. A budget that has run out also
/// clears itself when the integration next succeeds, so this is for putting one back before that
/// happens, or for handing back a total that is spent but not yet exhausted.
/// </summary>
[HandlerName("resetusage")]
public class ResetUsage(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RetryPolicyResetUsage, object>
{
    public async Task<object> Handle(int key, RetryPolicyResetUsage request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.Edit);

        var policy = await dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        // Scope the reset to this policy's own integrations and groups, so a policy id in the
        // route can never clear a counter belonging to a different policy.
        var subscriptionIds = await dbContext.Set<Subscription>()
            .Where(s => s.RetryPolicyId == key)
            .Select(s => s.Id)
            .ToListAsync();

        var groupIds = policy.Groups.Select(g => g.Id).ToList();

        var query = dbContext.Set<RetryGroupUsage>()
            .Where(u => subscriptionIds.Contains(u.SubscriptionId) && groupIds.Contains(u.GroupId));

        if (request.SubscriptionId.HasValue)
            query = query.Where(u => u.SubscriptionId == request.SubscriptionId.Value);

        if (request.GroupId.HasValue)
            query = query.Where(u => u.GroupId == request.GroupId.Value);

        await query.ExecuteDeleteAsync();

        // A budget put back by hand frees what was held for it, as a success would.
        await DelayedRetry.ReleaseHeldAsync(dbContext, request.SubscriptionId is { } one
            ? subscriptionIds.Where(id => id == one).ToList()
            : subscriptionIds);
        return null;
    }
}
