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
    private readonly AdapterSecretProperties _secrets;

    public Usage(BitweenDbContext dbContext, RequestContext requestContext,
        AdapterSecretProperties secrets)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
        _secrets = secrets;
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

        var subscriptionIds = subscriptions.Select(s => s.Id).ToList();

        var usages = await _dbContext.Set<RetryGroupUsage>().AsNoTracking()
            .Where(u => subscriptionIds.Contains(u.SubscriptionId))
            .ToListAsync();

        var overrides = await _dbContext.Set<RetryAlertOverride>().AsNoTracking()
            .Where(o => subscriptionIds.Contains(o.SubscriptionId))
            .ToListAsync();

        // Only groups that allow retries have a budget to spend — and a group that can never spend
        // one can never exhaust it, so it can never alert either. Listing those would invite
        // configuring an alert that cannot fire. A ceiling of zero counts as "never": TryConsume
        // denies it outright rather than claiming and exhausting it.
        var groups = policy.Groups
            .Where(g => g.Budget is { MaxAttemptsTotal: > 0 })
            .ToList();

        var rows = new List<RetryGroupUsageRow>();

        // Keyed once rather than scanned per pair: both lists are already keyed by exactly this
        // pair, and a policy shared by many subscriptions turns the scan into the cost of the
        // whole request.
        var usageByPair = usages.ToDictionary(u => (u.SubscriptionId, u.GroupId));
        var overrideByPair = overrides.ToDictionary(o => (o.SubscriptionId, o.GroupId));

        foreach (var subscription in subscriptions)
        foreach (var group in groups)
        {
            usageByPair.TryGetValue((subscription.Id, group.Id), out var usage);
            overrideByPair.TryGetValue((subscription.Id, group.Id), out var subscriptionOverride);

            var target = RetryAlertResolver.Resolve(subscriptionOverride, group, policy);

            // Mirrors the order the resolver walks, so the reported reason is the level that
            // actually decided: an override silences before the group is consulted at all.
            var silencedAt = subscriptionOverride?.AlertMode == RetryAlertMode.Silent
                ? RetryAlertLevel.SubscriptionGroup
                : group.AlertMode == RetryAlertMode.Silent
                    ? RetryAlertLevel.Group
                    : (RetryAlertLevel?)null;

            rows.Add(new RetryGroupUsageRow
            {
                SubscriptionId = subscription.Id,
                SubscriptionName = subscription.Name,
                GroupId = group.Id,
                GroupName = group.Name,
                AttemptsUsed = usage?.AttemptsUsed ?? 0,
                MaxAttemptsTotal = group.Budget!.MaxAttemptsTotal,
                Exhausted = usage != null && usage.AttemptsUsed >= group.Budget.MaxAttemptsTotal,
                LastAttemptOn = usage?.LastAttemptOn,
                ExhaustedNotifiedOn = usage?.ExhaustedNotifiedOn,
                AlertMode = subscriptionOverride?.AlertMode ?? RetryAlertMode.Inherit,
                OverrideHandlerId = subscriptionOverride?.AlertHandlerId,
                OverrideHandlerProperties = await _secrets.Mask(
                    subscriptionOverride?.AlertHandlerId, subscriptionOverride?.AlertHandlerProperties),
                ResolvedHandlerId = target?.HandlerId,
                ResolvedHandlerProperties = await _secrets.Mask(
                    target?.HandlerId, target?.HandlerProperties),
                ResolvedFrom = target?.Level,
                SilencedAt = target == null ? silencedAt : null
            });
        }

        return rows
            // Worst first: stopped retrying, then alerting nowhere, then whatever has spent most.
            .OrderByDescending(r => r.Exhausted)
            .ThenBy(r => r.ResolvedHandlerId != null)
            .ThenByDescending(r => r.AttemptsUsed)
            .ThenBy(r => r.SubscriptionName)
            .ThenBy(r => r.GroupName)
            .ToList();
    }
}
