using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>What a channel's adapter is sent when a subscription pauses itself.</summary>
public class SubscriptionAutoPausedNotification
{
    public string Kind => "SubscriptionAutoPaused";
    public int SubscriptionId { get; set; }
    public string SubscriptionName { get; set; }
    public int Failures { get; set; }
    public string XchangeId { get; set; }
    public string Exception { get; set; }
    public DateTime OccurredOn { get; set; }
}

/// <summary>
/// Tells someone a subscription paused itself: through its retry policy's alert channel, the place
/// already set up to hear that deliveries stopped going through, and through the subscription's own
/// notifications ticked for failures. A channel that is both hears once. With neither, the pause is
/// logged and shown on the subscription's page, and nothing is sent.
/// </summary>
public class AutoPauseAlertService(
    BitweenDbContext dbContext,
    NotificationChannelSender channelSender,
    ILogger<AutoPauseAlertService> logger) : IConsume<SubscriptionAutoPausedEvent>
{
    public async Task Process(SubscriptionAutoPausedEvent message)
    {
        var subscription = await dbContext.Set<Subscription>().AsNoTracking()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == message.SubscriptionId);
        if (subscription == null) return;

        var channelIds = (subscription.Notifications ?? []).Where(n => n.OnFailure).Select(n => n.ChannelId).ToList();
        if (RetryAlertResolver.Resolve(null, null, subscription.RetryPolicy) is { } target)
            channelIds.Insert(0, target.ChannelId);

        // At-least-once delivery: a successful row says that channel already heard.
        var told = await dbContext.Set<XchangeNotification>()
            .Where(n => n.XchangeId == message.XchangeId
                        && n.NotifierName == XchangeNotification.AutoPauseAlertName
                        && n.Success && n.ChannelId != null)
            .Select(n => n.ChannelId!.Value)
            .ToListAsync();
        var toTell = channelIds.Distinct().Except(told).ToList();
        if (toTell.Count == 0) return;

        var notification = new SubscriptionAutoPausedNotification
        {
            SubscriptionId = message.SubscriptionId,
            SubscriptionName = subscription.Name,
            Failures = message.Failures,
            XchangeId = message.XchangeId,
            Exception = subscription.LastException,
            OccurredOn = message.OccurredOn
        };
        var partnerId = await dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => x.Id == message.XchangeId)
            .Select(x => x.PartnerId)
            .FirstOrDefaultAsync() ?? subscription.PartnerId;
        var partner = partnerId is { } id ? await dbContext.FindAsync<Partner>(id) : null;

        foreach (var channelId in toTell)
        {
            var error = await channelSender.Send(channelId, notification, message.XchangeId, message.XchangeId,
                partner);
            if (error != null)
                logger.LogError(error,
                    "Auto-pause alert for subscription {SubscriptionId} could not be delivered through channel {ChannelId}.",
                    message.SubscriptionId, channelId);

            dbContext.Add(XchangeNotification.ForAutoPauseAlert(message.XchangeId, channelId, error?.ToString()));
            await dbContext.SaveChangesAsync();
        }
    }
}
