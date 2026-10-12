using System;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween;

/// <summary>
/// Hands one notification to a <see cref="NotificationChannel"/>'s adapter. Every place that sends
/// through a channel comes here, so a channel delivers the same way whatever asked it to.
/// </summary>
public class NotificationChannelSender(IInfolinkCache cache, IAdapterInvoker adapterInvoker)
{
    /// <summary>
    /// Null once delivered; otherwise what the last attempt threw. Never throws itself: the caller
    /// records the outcome, because "did anyone get told?" has to be answerable afterwards.
    /// </summary>
    /// <param name="partner">
    /// The partner the reported exchange ran for, which <c>{{partner.…}}</c> in the channel's
    /// settings is filled from, so one channel can, say, email each partner's own contact.
    /// </param>
    public async Task<Exception> Send(NotificationChannel channel, object payload, string xchangeId,
        string correlationId, Partner partner = null)
    {
        try
        {
            // The same filling every subscription adapter gets, so a channel can keep a shared mail
            // password in a global value set instead of a copy in every channel.
            var properties = channel.HandlerProperties.ToDictionary()
                .Fill(partner, await cache.ListGlobalAdapterValuesSetsAsync());

            return await NotifierDelivery.SendAsync(adapterInvoker, channel.HandlerId, properties, payload,
                xchangeId, xchangeId, correlationId);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
