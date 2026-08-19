using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Reports the state of every subscription-and-group pair using this policy: how much of the
/// group's total budget that subscription has spent, and where the pair's budget-exhausted alert
/// would go.
/// </summary>
/// <remarks>
/// <para>
/// Both halves share the <c>(SubscriptionId, GroupId)</c> key, so they are reported together — the
/// question worth asking about an exhausted budget is whether anyone was told about it, and
/// splitting that across two reports leaves the caller to join them by eye.
/// </para>
/// <para>
/// Starts from the policy's groups rather than from the stored counters, so a subscription that has
/// never failed still gets a row and its alert override stays configurable before the first failure.
/// Counters for groups no longer in the policy are therefore left out, which is what
/// <see cref="Update"/> and <see cref="Delete"/> delete outright.
/// </para>
/// </remarks>
[HandlerName("usage")]
public class Usage : ICommandHandler<int, RetryPolicyUsageRequest, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;
    private readonly RetryUsageReport _report;

    public Usage(BitweenDbContext dbContext, RequestContext requestContext, RetryUsageReport report)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
        _report = report;
    }

    public async Task<object> Handle(int key, RetryPolicyUsageRequest request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        var policy = await _dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        var subscriptions = await _dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.RetryPolicyId == key)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        return await _report.Build(
            subscriptions.Select(s => (s.Id, s.Name)).ToList(), policy.Groups, policy);
    }
}
