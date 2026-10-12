using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween;

/// <summary>
/// Hands one notification to a <see cref="NotificationChannel"/>'s adapter. Every place that sends
/// through a channel comes here, so a channel delivers the same way whatever asked it to.
/// </summary>
public class NotificationChannelSender(IInfolinkCache cache, IAdapterInvoker adapterInvoker,
    BitweenOptions options, IHttpContextAccessor httpContextAccessor)
{
    /// <summary>
    /// Null once delivered; otherwise what the last attempt threw. Never throws itself: the caller
    /// records the outcome, because "did anyone get told?" has to be answerable afterwards.
    /// </summary>
    /// <param name="partner">
    /// The partner the reported exchange ran for, which <c>{{partner.…}}</c> in the channel's
    /// settings is filled from, so one channel can, say, email each partner's own contact.
    /// </param>
    public Task<Exception> Send(NotificationChannel channel, object payload, string xchangeId,
        string correlationId, Partner partner = null) =>
        Deliver(channel, payload, xchangeId, correlationId, partner, attempts: 3);

    /// <summary>
    /// Sends a made-up notification, once, so whoever set the channel up can see one arrive. Nothing
    /// is recorded: there is no exchange to record it against.
    /// </summary>
    /// <remarks>
    /// An Email, Teams or Slack channel says outright that it is a test. Any other adapter gets a
    /// notification shaped like a real failure, because a template written against those fields is
    /// part of what is being tested; its subscription name says it is a test.
    /// </remarks>
    public Task<Exception> SendTest(NotificationChannel channel)
    {
        const string testId = "test";
        var now = DateTime.UtcNow;
        object payload = ChannelMessages.AdapterIds.Contains(channel.HandlerId)
            ? ChannelMessages.Test(channel.Name)
            : new XchangeResultNotification
            {
                Id = testId,
                Success = false,
                Exception = "This is a test notification from Bitween. Nothing failed.",
                StartedOn = now,
                FinishedOn = now,
                SubscriptionName = "Test notification",
                DocumentName = "Test",
                CorrelationId = testId
            };

        return Deliver(channel, payload, testId, testId, null, attempts: 1);
    }

    private async Task<Exception> Deliver(NotificationChannel channel, object payload, string xchangeId,
        string correlationId, Partner partner, int attempts)
    {
        try
        {
            // The same filling every subscription adapter gets, so a channel can keep a shared mail
            // password in a global value set instead of a copy in every channel.
            var properties = channel.HandlerProperties.ToDictionary()
                .Fill(partner, await cache.ListGlobalAdapterValuesSetsAsync());

            var data = ChannelMessages.AdapterIds.Contains(channel.HandlerId)
                ? ChannelMessages.For(payload, LinkBase)
                : payload;

            return await NotifierDelivery.SendAsync(adapterInvoker, channel.HandlerId, properties, data,
                xchangeId, xchangeId, correlationId, attempts);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Where a message's link points: the public address the chart sets, or else the address the
    /// current request came in on, the test button's. A notification sent from a queue has no
    /// request, and the instance's own address would be no use to whoever reads it, so it goes
    /// without a link.
    /// </summary>
    private string LinkBase
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options.PublicUrl)) return options.PublicUrl.TrimEnd('/');

            var request = httpContextAccessor.HttpContext?.Request;
            return request is { Host.HasValue: true }
                ? $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/')
                : null;
        }
    }
}
