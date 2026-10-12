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
/// Read in memory rather than queried, because the uses sit inside JSON columns that no database
/// here can search alike. They are configuration, so the lists stay small.
/// </remarks>
public static class NotificationChannelUsage
{
    public static async Task<Dictionary<int, List<NotificationChannelUse>>> Find(BitweenDbContext dbContext)
    {
        var subscriptions = await dbContext.Set<Subscription>().AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Notifications })
            .ToListAsync();

        return subscriptions
            .SelectMany(s => (s.Notifications ?? []).Select(n => n.ChannelId).Distinct()
                .Select(channelId => (ChannelId: channelId, Use: new NotificationChannelUse
                {
                    Kind = NotificationChannelUseKind.Subscription,
                    SubscriptionId = s.Id,
                    Name = s.Name
                })))
            .GroupBy(u => u.ChannelId)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Use).ToList());
    }
}
