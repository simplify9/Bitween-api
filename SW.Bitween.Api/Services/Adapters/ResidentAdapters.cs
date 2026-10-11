using Microsoft.Extensions.DependencyInjection;
using SW.Serverless;
using System;
using System.Threading.Tasks;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// Whether an adapter is the long-lived kind, read from its published metadata.
///
/// It lives here because two services outside the runtime need to ask, and both got the answer
/// wrong in the same way: they probed the adapter by SPAWNING it down the classic stdio path. A
/// resident adapter dials out instead of speaking stdio, so that probe never gets an answer - one
/// caller failed as "Received null data" and refused to save the subscription at all, the other
/// failed closed and masked every property as a secret. Neither failure named a cause.
/// </summary>
public static class ResidentAdapters
{
    /// <summary>
    /// False for anything whose metadata cannot be read, so an unknown adapter keeps whatever
    /// behaviour it had before this existed.
    /// </summary>
    public static async Task<bool> IsResidentAsync(IServiceProvider serviceProvider, string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) return false;

        try
        {
            // Cached by the installer, so this is not a storage round trip per call.
            var installer = serviceProvider.GetRequiredService<AdapterInstaller>();
            var metadata = await installer.GetMetadataAsync(adapterId);

            return metadata?.AdapterValues != null &&
                   metadata.AdapterValues.TryGetValue(ResidentAdapterRuntime.LifecycleKey, out var lifecycle) &&
                   string.Equals(lifecycle, ResidentAdapterRuntime.ResidentValue,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
