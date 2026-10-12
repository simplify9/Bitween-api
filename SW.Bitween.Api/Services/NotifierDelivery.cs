using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Hands a notifier's handler one <see cref="XchangeResultNotification"/>. Shared by exchange results
/// and the alerts that have no exchange, so all reach a notifier the same way, quick retries included.
/// </summary>
public static class NotifierDelivery
{
    /// <summary>
    /// Null once delivered; otherwise what the last attempt threw. <paramref name="xchangeId"/> fills
    /// the handler's <c>xchangeid</c> property; an alert without an exchange passes null and names the
    /// file after its own id instead.
    /// </summary>
    public static async Task<Exception> SendAsync(IAdapterInvoker adapterInvoker, Notifier notifier,
        XchangeResultNotification notification, string xchangeId, string correlationId)
    {
        var handlerProperties = notifier.HandlerProperties.ToDictionary();
        if (xchangeId != null) handlerProperties["xchangeid"] = xchangeId;

        // A couple of quick retries for a mail server that hiccups; a send that failed used to be
        // recorded and never tried again.
        Exception lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            try
            {
                await adapterInvoker.InvokeAsync<XchangeFile>(
                    notifier.HandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
                    new XchangeFile(JsonConvert.SerializeObject(notification), xchangeId ?? notification.Id),
                    handlerProperties, correlationId);
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        return lastError;
    }
}
