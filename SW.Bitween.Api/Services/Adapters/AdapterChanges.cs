using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Adapters;

/// <summary>Told to every node when an adapter's published versions change.</summary>
public class AdapterChangedMessage
{
    public string AdapterId { get; set; }
}

/// <summary>
/// Forgets what this node and every other has cached about an adapter: its catalog entry and its
/// described properties. Each node caches both on its own, so forgetting only on the node that
/// published left the others showing the old version and its old properties until their caches
/// expired. The broadcast is best effort, like the configuration cache's: the publish has happened
/// whether or not the bus could be told, and the caches still expire on their own.
/// </summary>
public class AdapterChanges(AdapterCatalog catalog, AdapterStartupValues startupValues,
    IServiceScopeFactory scopes, ILogger<AdapterChanges> logger)
{
    public async Task ForgetEverywhereAsync(string adapterId)
    {
        Forget(adapterId);
        try
        {
            using var scope = scopes.CreateScope();
            var broadcast = scope.ServiceProvider.GetService<IBroadcast>();
            if (broadcast != null) await broadcast.Broadcast(new AdapterChangedMessage { AdapterId = adapterId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't tell the other nodes that {AdapterId} changed; they show it as cached until it expires.", adapterId);
        }
    }

    public void Forget(string adapterId)
    {
        catalog.Forget(adapterId);
        startupValues.Forget(adapterId);
    }
}

public class AdapterChangedListener(AdapterChanges changes) : IListen<AdapterChangedMessage>
{
    public Task Process(AdapterChangedMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message?.AdapterId)) changes.Forget(message.AdapterId);
        return Task.CompletedTask;
    }
}
