using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Reports how much of each group's total budget the integrations using this policy have spent,
/// so an exhausted group is visible instead of just silently declining to retry.
/// </summary>
[HandlerName("usage")]
public class Usage : ICommandHandler<int, RetryPolicyUsageRequest, object>
{
    private readonly BitweenDbContext _dbContext;

    public Usage(BitweenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(int key, RetryPolicyUsageRequest request)
    {
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

        // Only groups that allow retries have a budget to spend.
        var budgets = policy.Groups
            .Where(g => g.Budget != null)
            .ToDictionary(g => g.Id, g => new { g.Name, g.Budget.MaxAttemptsTotal });

        var names = subscriptions.ToDictionary(s => s.Id, s => s.Name);

        var rows = usages
            .Where(u => budgets.ContainsKey(u.GroupId))
            .Select(u => new RetryGroupUsageRow
            {
                SubscriptionId = u.SubscriptionId,
                SubscriptionName = names.GetValueOrDefault(u.SubscriptionId),
                GroupId = u.GroupId,
                GroupName = budgets[u.GroupId].Name,
                AttemptsUsed = u.AttemptsUsed,
                MaxAttemptsTotal = budgets[u.GroupId].MaxAttemptsTotal,
                Exhausted = u.AttemptsUsed >= budgets[u.GroupId].MaxAttemptsTotal,
                LastAttemptOn = u.LastAttemptOn
            })
            // Exhausted integrations first — those are the ones no longer being retried.
            .OrderByDescending(r => r.Exhausted)
            .ThenByDescending(r => r.AttemptsUsed)
            .ToList();

        return new List<RetryGroupUsageRow>(rows);
    }
}
