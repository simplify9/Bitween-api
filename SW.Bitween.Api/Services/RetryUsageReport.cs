using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// Builds the state of subscription-and-group pairs — spent budget, and where the pair's
/// budget-exhausted alert would go.
/// </summary>
/// <remarks>
/// Shared because the same pairs are asked about from two directions: a policy wants every
/// subscription using it, and a subscription wants its own, whether its policy is a shared one or an
/// inline <c>CustomRetryPolicy</c> that no policy id can reach. Two copies of alert resolution and
/// secret masking would drift, and the half that drifted would be the half that leaks.
/// </remarks>
public class RetryUsageReport(BitweenDbContext dbContext, AdapterSecretProperties secrets)
{
    /// <summary>
    /// One row per subscription and per group that could actually exhaust.
    /// </summary>
    /// <param name="subscriptions">The pairs' subscriptions, with the names to report.</param>
    /// <param name="allGroups">Every group of the applicable policy; the unusable ones are dropped here.</param>
    /// <param name="policy">
    /// The shared policy, or <c>null</c> for an inline one — which has no row to carry a
    /// policy-level alert, so those pairs can only resolve to a group or an override.
    /// </param>
    public async Task<List<RetryGroupUsageRow>> Build(
        IReadOnlyList<(int Id, string Name)> subscriptions,
        IReadOnlyList<RetryGroup> allGroups,
        RetryPolicy policy)
    {
        // Only groups that allow retries have a budget to spend — and a group that can never spend
        // one can never exhaust it, so it can never alert either. Listing those would invite
        // configuring an alert that cannot fire. A ceiling of zero counts as "never": TryConsume
        // denies it outright rather than claiming and exhausting it.
        var groups = allGroups.Where(g => g.Budget is { MaxAttemptsTotal: > 0 }).ToList();
        if (groups.Count == 0 || subscriptions.Count == 0) return [];

        var subscriptionIds = subscriptions.Select(s => s.Id).ToList();

        var usages = await dbContext.Set<RetryGroupUsage>().AsNoTracking()
            .Where(u => subscriptionIds.Contains(u.SubscriptionId))
            .ToListAsync();

        var overrides = await dbContext.Set<RetryAlertOverride>().AsNoTracking()
            .Where(o => subscriptionIds.Contains(o.SubscriptionId))
            .ToListAsync();

        // What became of the alerts that were raised. The counter records only that one was
        // claimed — claiming is what stops a redelivery sending it twice, and it happens before
        // the send is tried — so whether anyone was actually told lives here instead, in the
        // delivery log. The pair is reconstructed the same way the alert reached it: the exchange
        // names the integration, its result names the group.
        var alertLog = await (
                from notification in dbContext.Set<XchangeNotification>().AsNoTracking()
                where notification.NotifierName == XchangeNotification.RetryBudgetAlertName
                join xchange in dbContext.Set<Xchange>().AsNoTracking()
                    on notification.XchangeId equals xchange.Id
                join result in dbContext.Set<XchangeResult>().AsNoTracking()
                    on notification.XchangeId equals result.Id
                where xchange.SubscriptionId != null
                      && subscriptionIds.Contains(xchange.SubscriptionId.Value)
                      && result.RetryGroupId != null
                select new
                {
                    SubscriptionId = xchange.SubscriptionId!.Value,
                    GroupId = result.RetryGroupId!.Value,
                    notification.Success,
                    notification.Exception,
                    notification.FinishedOn
                })
            .ToListAsync();

        // One success is delivery, however many failures preceded it — the same rule the sender
        // itself applies when it decides an alert has already gone out. Failing that, the most
        // recent failure is the outcome.
        var alertByPair = alertLog
            .GroupBy(a => (a.SubscriptionId, a.GroupId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.Success).ThenByDescending(a => a.FinishedOn).First());

        // Keyed once rather than scanned per pair: both lists are already keyed by exactly this
        // pair, and a policy shared by many subscriptions turns the scan into the cost of the
        // whole request.
        var usageByPair = usages.ToDictionary(u => (u.SubscriptionId, u.GroupId));
        var overrideByPair = overrides.ToDictionary(o => (o.SubscriptionId, o.GroupId));

        var rows = new List<RetryGroupUsageRow>();

        foreach (var subscription in subscriptions)
        foreach (var group in groups)
        {
            usageByPair.TryGetValue((subscription.Id, group.Id), out var usage);
            overrideByPair.TryGetValue((subscription.Id, group.Id), out var subscriptionOverride);
            alertByPair.TryGetValue((subscription.Id, group.Id), out var alert);

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
                // Only meaningful once an alert has been claimed. A delivery row surviving from
                // before a reset would otherwise report an outcome for an alert that has since
                // been re-armed and not raised again.
                AlertDelivered = usage?.ExhaustedNotifiedOn == null ? null : alert?.Success,
                AlertError = usage?.ExhaustedNotifiedOn != null && alert is { Success: false }
                    ? alert.Exception
                    : null,
                AlertMode = subscriptionOverride?.AlertMode ?? RetryAlertMode.Inherit,
                OverrideHandlerId = subscriptionOverride?.AlertHandlerId,
                OverrideHandlerProperties = await secrets.Mask(
                    subscriptionOverride?.AlertHandlerId, subscriptionOverride?.AlertHandlerProperties),
                ResolvedHandlerId = target?.HandlerId,
                ResolvedHandlerProperties = await secrets.Mask(
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
