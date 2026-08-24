using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Reports one subscription's spent retry budget and where each group's exhaustion alert would go.
/// </summary>
/// <remarks>
/// The policy-scoped report answers the same question for every subscription sharing a policy, but it
/// can only find subscriptions by policy id — so a subscription carrying an inline
/// <c>CustomRetryPolicy</c> is invisible to it while still spending and recording budget. Asking from
/// the subscription's side reaches those too.
/// </remarks>
[HandlerName("retryusage")]
public class RetryUsage : ICommandHandler<int, RetryPolicyUsageRequest, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;
    private readonly RetryUsageReport _report;

    public RetryUsage(BitweenDbContext dbContext, RequestContext requestContext, RetryUsageReport report)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
        _report = report;
    }

    public async Task<object> Handle(int key, RetryPolicyUsageRequest request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

        var subscription = await _dbContext.Set<Subscription>().AsNoTracking()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == key);
        if (subscription == null) throw new SWNotFoundException(key.ToString());

        // Whichever policy actually applies. An inline one has no row, so the policy level of the
        // alert hierarchy simply is not there for it — passed as null, which the resolver expects.
        var groups = subscription.CustomRetryPolicy?.Groups ?? subscription.RetryPolicy?.Groups ?? [];

        return await _report.Build(
            [(subscription.Id, subscription.Name)], groups, subscription.RetryPolicy);
    }
}
