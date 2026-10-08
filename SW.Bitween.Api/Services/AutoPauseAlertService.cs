using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>What the alert handler is sent when a subscription pauses itself.</summary>
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
/// Tells someone a subscription paused itself, through the alert handler of its retry policy — the
/// place already set up to hear that deliveries stopped going through. A subscription with no such
/// policy, or one with no alert handler, has the pause logged and shown on its page, and nothing sent.
/// </summary>
public class AutoPauseAlertService(
    BitweenDbContext dbContext,
    IAdapterInvoker adapterInvoker,
    ILogger<AutoPauseAlertService> logger) : IConsume<SubscriptionAutoPausedEvent>
{
    public async Task Process(SubscriptionAutoPausedEvent message)
    {
        // At-least-once delivery: a successful row says it already went out.
        var alreadySent = await dbContext.Set<XchangeNotification>()
            .AnyAsync(n => n.XchangeId == message.XchangeId
                           && n.NotifierName == XchangeNotification.AutoPauseAlertName
                           && n.Success);
        if (alreadySent) return;

        var subscription = await dbContext.Set<Subscription>().AsNoTracking()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == message.SubscriptionId);
        var target = RetryAlertResolver.Resolve(null, null, subscription?.RetryPolicy);
        if (target == null) return;

        var notification = new SubscriptionAutoPausedNotification
        {
            SubscriptionId = message.SubscriptionId,
            SubscriptionName = subscription!.Name,
            Failures = message.Failures,
            XchangeId = message.XchangeId,
            Exception = subscription.LastException,
            OccurredOn = message.OccurredOn
        };
        var handlerProperties = new Dictionary<string, string>(
            target.HandlerProperties ?? new Dictionary<string, string>())
        {
            ["xchangeid"] = message.XchangeId
        };

        try
        {
            await adapterInvoker.InvokeAsync<XchangeFile>(
                target.HandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
                new XchangeFile(JsonConvert.SerializeObject(notification), message.XchangeId),
                handlerProperties, message.XchangeId);
            dbContext.Add(XchangeNotification.ForAutoPauseAlert(message.XchangeId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-pause alert for subscription {SubscriptionId} could not be delivered through {HandlerId}.",
                message.SubscriptionId, target.HandlerId);
            dbContext.Add(XchangeNotification.ForAutoPauseAlert(message.XchangeId, ex.ToString()));
        }

        await dbContext.SaveChangesAsync();
    }
}
