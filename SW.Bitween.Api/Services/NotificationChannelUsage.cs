using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// Every place that sends through a notification channel, keyed by channel id.
/// </summary>
/// <remarks>
/// Read in memory rather than queried, because most uses sit inside JSON columns — a subscription's
/// notifications, a policy's groups, an inline policy's groups — that no database here can search
/// alike. Both lists are configuration, so they stay small.
/// </remarks>
public static class NotificationChannelUsage
{
    public static async Task<Dictionary<int, List<NotificationChannelUse>>> Find(BitweenDbContext dbContext)
    {
        var uses = new Dictionary<int, List<NotificationChannelUse>>();

        void Add(int? channelId, NotificationChannelUse use)
        {
            if (channelId == null) return;
            if (!uses.TryGetValue(channelId.Value, out var list))
                uses[channelId.Value] = list = [];
            list.Add(use);
        }

        var subscriptions = await dbContext.Set<Subscription>().AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Notifications, s.CustomRetryPolicy })
            .ToListAsync();

        var policies = await dbContext.Set<RetryPolicy>().AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.AlertChannelId, p.Groups })
            .ToListAsync();

        var overrides = await dbContext.Set<RetryAlertOverride>().AsNoTracking()
            .Where(o => o.AlertChannelId != null && o.AlertMode == RetryAlertMode.Send)
            .ToListAsync();

        foreach (var subscription in subscriptions)
        {
            foreach (var channelId in (subscription.Notifications ?? []).Select(n => n.ChannelId).Distinct())
                Add(channelId, new NotificationChannelUse
                {
                    Kind = NotificationChannelUseKind.Subscription,
                    SubscriptionId = subscription.Id,
                    Name = subscription.Name
                });

            foreach (var group in subscription.CustomRetryPolicy?.Groups ?? [])
                if (group.AlertMode == RetryAlertMode.Send)
                    Add(group.AlertChannelId, new NotificationChannelUse
                    {
                        Kind = NotificationChannelUseKind.RetryGroup,
                        SubscriptionId = subscription.Id,
                        Name = $"{subscription.Name} › {group.Name}"
                    });
        }

        foreach (var policy in policies)
        {
            Add(policy.AlertChannelId, new NotificationChannelUse
            {
                Kind = NotificationChannelUseKind.RetryPolicy,
                RetryPolicyId = policy.Id,
                Name = policy.Name
            });

            foreach (var group in policy.Groups ?? [])
                if (group.AlertMode == RetryAlertMode.Send)
                    Add(group.AlertChannelId, new NotificationChannelUse
                    {
                        Kind = NotificationChannelUseKind.RetryGroup,
                        RetryPolicyId = policy.Id,
                        Name = $"{policy.Name} › {group.Name}"
                    });
        }

        foreach (var alertOverride in overrides)
        {
            var subscription = subscriptions.FirstOrDefault(s => s.Id == alertOverride.SubscriptionId);
            var policy = policies.FirstOrDefault(p => (p.Groups ?? []).Any(g => g.Id == alertOverride.GroupId));
            var group = policy?.Groups.First(g => g.Id == alertOverride.GroupId);
            Add(alertOverride.AlertChannelId, new NotificationChannelUse
            {
                Kind = NotificationChannelUseKind.RetryAlertOverride,
                SubscriptionId = alertOverride.SubscriptionId,
                RetryPolicyId = policy?.Id,
                Name = $"{subscription?.Name ?? $"Subscription {alertOverride.SubscriptionId}"} › {group?.Name ?? "a removed group"}"
            });
        }

        return uses;
    }
}
