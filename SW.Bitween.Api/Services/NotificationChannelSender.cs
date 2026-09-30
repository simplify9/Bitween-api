using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Delivers one notification through a <see cref="NotificationChannel"/>. Every place that notifies
/// — a subscription's finished exchanges, an exhausted retry budget — goes through here, so a
/// channel sends the same way whatever asked it to.
/// </summary>
public class NotificationChannelSender(IInfolinkCache cache, IAdapterInvoker adapterInvoker)
{
    /// <summary>What happened to one send.</summary>
    /// <param name="ChannelName">Null when the channel no longer exists.</param>
    /// <param name="Error">Null when the handler accepted it.</param>
    public record Outcome(string ChannelName, string Error);

    /// <summary>
    /// Serialises <paramref name="payload"/> and hands it to the channel's handler with the
    /// channel's own properties plus <c>xchangeid</c>. Never throws for a failed send: the caller
    /// records the outcome, because "did anyone get told?" has to be answerable afterwards.
    /// </summary>
    public async Task<Outcome> Send(int channelId, object payload, string xchangeId, string correlationId)
    {
        var channel = await cache.NotificationChannelByIdAsync(channelId);

        // Deleting a channel that something still uses is refused, so this is a delete racing a
        // send. Recorded as a failure rather than skipped, so the gap shows up in the history.
        if (channel == null)
            return new Outcome(null, $"Notification channel {channelId} no longer exists.");

        var properties = new Dictionary<string, string>(
            channel.HandlerProperties ?? new Dictionary<string, string>())
        {
            ["xchangeid"] = xchangeId
        };

        try
        {
            await adapterInvoker.InvokeAsync<XchangeFile>(
                channel.HandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
                new XchangeFile(JsonConvert.SerializeObject(payload), xchangeId),
                properties, correlationId ?? xchangeId);
            return new Outcome(channel.Name, null);
        }
        catch (Exception ex)
        {
            return new Outcome(channel.Name, ex.ToString());
        }
    }
}
