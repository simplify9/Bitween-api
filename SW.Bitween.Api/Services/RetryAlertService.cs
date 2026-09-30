using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Delivers "retry budget exhausted" alerts, on its own queue.
/// </summary>
/// <remarks>
/// Deliberately a separate consumer rather than another branch inside <c>XchangeService</c>'s
/// result handling: alerts go through a customer-configured adapter that may be slow or broken, and
/// on the shared result queue that would hold up — or fail — the ordinary notifiers for the same
/// exchange. Its own <see cref="IConsume{TMessage}"/> means its own queue, and the two fail apart.
/// </remarks>
public class RetryAlertService(
    BitweenDbContext dbContext,
    NotificationChannelSender channelSender,
    ILogger<RetryAlertService> logger) : IConsume<RetryBudgetExhaustedEvent>
{
    public async Task Process(RetryBudgetExhaustedEvent message)
    {
        // The bus is at-least-once, and the exhaustion is stamped on the exchange rather than on
        // the send, so a redelivery would otherwise email the same alert twice. A *successful* log
        // row is the record that it already went out — matching any alert row would let one failed
        // send stand in for a delivery and silence every later attempt. The name is checked too, so
        // a row written by some other path can never be mistaken for this alert.
        var alreadySent = await dbContext.Set<XchangeNotification>()
            .AnyAsync(n => n.XchangeId == message.XchangeId
                           && n.NotifierName == XchangeNotification.RetryBudgetAlertName
                           && n.Success);
        if (alreadySent) return;

        var subscription = await dbContext.Set<Subscription>()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == message.SubscriptionId);
        if (subscription == null) return;

        // An inline custom policy has no policy row, so only the group and override levels of the
        // hierarchy can configure an alert for it.
        IRetryPolicy policy = subscription.CustomRetryPolicy ?? (IRetryPolicy)subscription.RetryPolicy;
        var group = policy?.Groups?.FirstOrDefault(g => g.Id == message.GroupId);

        var subscriptionOverride = await dbContext.Set<RetryAlertOverride>()
            .FirstOrDefaultAsync(o => o.SubscriptionId == message.SubscriptionId
                                      && o.GroupId == message.GroupId);

        var target = RetryAlertResolver.Resolve(subscriptionOverride, group, subscription.RetryPolicy);
        if (target == null) return;

        var notification = await BuildNotification(message, subscription);
        await Send(target, notification, message.XchangeId);
    }

    private async Task<RetryBudgetExhaustedNotification> BuildNotification(
        RetryBudgetExhaustedEvent message, Subscription subscription)
    {
        var context = await (from xchange in dbContext.Set<Xchange>().AsNoTracking()
                where xchange.Id == message.XchangeId
                join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                from result in xr.DefaultIfEmpty()
                select new
                {
                    document.Name,
                    xchange.CorrelationId,
                    result.Exception,
                    result.RetryBlockedReason
                })
            .FirstOrDefaultAsync();

        return new RetryBudgetExhaustedNotification
        {
            XchangeId = message.XchangeId,
            SubscriptionId = message.SubscriptionId,
            SubscriptionName = subscription.Name,
            DocumentName = context?.Name,
            CorrelationId = context?.CorrelationId,
            PolicyName = subscription.RetryPolicy?.Name,
            GroupName = message.GroupName,
            MaxAttemptsTotal = message.MaxAttemptsTotal,
            BlockedReason = context?.RetryBlockedReason,
            Exception = context?.Exception,
            OccurredOn = message.OccurredOn
        };
    }

    /// <summary>
    /// Sends through the resolved channel and records the attempt either way.
    /// </summary>
    /// <remarks>
    /// A failure is logged rather than thrown, and recorded so someone can answer "did the alert
    /// actually go out?". Because the guard above only counts a successful row, a failed send
    /// leaves the way open for a redelivery to try again rather than closing it.
    /// </remarks>
    private async Task Send(RetryAlertTarget target, RetryBudgetExhaustedNotification notification,
        string xchangeId)
    {
        var outcome = await channelSender.Send(target.ChannelId, notification, xchangeId,
            notification.CorrelationId);

        if (outcome.Error != null)
            logger.LogError(
                "Retry budget alert for xchange {XchangeId} could not be delivered through channel {ChannelId}: {Error}",
                xchangeId, target.ChannelId, outcome.Error);

        dbContext.Add(XchangeNotification.ForRetryBudgetAlert(xchangeId, target.ChannelId, outcome.Error));
        await dbContext.SaveChangesAsync();
    }
}
