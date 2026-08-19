using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Runs a handler adapter, whichever kind it is: in-process for a native handler, or through
/// serverless for an uploaded one.
/// </summary>
/// <remarks>
/// The choice between the two is made from the id alone and is identical wherever a handler is
/// invoked, so it lives here once. Kept as the single place that knows the adapter contract — when
/// that contract changes, a copy of this block somewhere else is what gets left behind.
/// </remarks>
public class AdapterInvoker(
    NativeAdapterDiscoveryService nativeAdapterDiscovery,
    IServiceProvider serviceProvider)
{
    /// <summary>
    /// Hands <paramref name="payload"/> to the handler and returns whatever it produced, which is
    /// <c>null</c> for a handler that only consumes.
    /// </summary>
    public async Task<XchangeFile> Handle(string handlerId, Dictionary<string, string> handlerProperties,
        string correlationId, XchangeFile payload)
    {
        if (handlerId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var handler = nativeAdapterDiscovery.GetNativeHandler(handlerId, handlerProperties);
            return await handler.Handle(payload);
        }

        var serverless = serviceProvider.GetRequiredService<IServerlessService>();
        await serverless.StartAsync(handlerId, correlationId, handlerProperties);
        return await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), payload);
    }
}
