using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public class RevokeCacheMessage
{
}

public class InMemoryBitweenCache : IInfolinkCache
{
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _ssf;
    private readonly ILogger<InMemoryBitweenCache> _logger;

    /// <summary>How many times <see cref="Load"/> re-reads before publishing regardless.</summary>
    private const int MaxLoadAttempts = 3;

    /// <summary>Bumped by every <see cref="Revoke"/>, so a load can tell one overtook it.</summary>
    private long _generation;


    public InMemoryBitweenCache(IMemoryCache memoryCache, IServiceScopeFactory ssf,
        ILogger<InMemoryBitweenCache> logger)
    {
        _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _ssf = ssf ?? throw new ArgumentNullException(nameof(ssf));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads every cached set from the database and publishes it.
    /// </summary>
    /// <remarks>
    /// The six reads take time, and <see cref="Revoke"/> runs on the bus consumer thread against
    /// this same singleton. A revoke landing between the reads and the writes is revoking the
    /// snapshot those reads just took — the write it announces happened after they began — so
    /// publishing it anyway would reinstate the staleness the revoke existed to clear, for the
    /// full ten minutes. Hence the generation check: read again rather than publish a snapshot
    /// that is already known to be behind.
    ///
    /// Callers rely on this having populated the cache by the time it returns, so the last attempt
    /// publishes regardless. Two revokes landing inside one set of reads is already unlikely; three
    /// means the system is revoking continuously, and making progress matters more than the last
    /// few milliseconds of freshness.
    /// </remarks>
    private async Task Load()
    {
        for (var attempt = 0; ; attempt++)
        {
            var generation = Volatile.Read(ref _generation);

            using var scope = _ssf.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            _logger.LogInformation("Loading documents and subscriptions to cache");
            var cachedSubscriptions = await repo.Set<Subscription>().Include(s=>s.WorkGroup)
                .AsNoTracking().Where(i => !i.Inactive).ToArrayAsync();
            var cachedDocuments = await repo.Set<Document>().AsNoTracking().ToArrayAsync();
            var cachedNotifiers = await repo.Set<Notifier>().Where(i => !i.Inactive).AsNoTracking().ToArrayAsync();
            var cachedWorkGroups = await repo.Set<WorkGroup>().AsNoTracking().ToArrayAsync();
            var cachedGlobalValues = await repo.Set<GlobalAdapterValuesSet>().AsNoTracking().ToArrayAsync();
            var cachedBusGateways = await repo.Set<BusGateway>().Include(g => g.Routes).AsNoTracking().ToArrayAsync();

            if (Volatile.Read(ref _generation) != generation && attempt < MaxLoadAttempts - 1)
            {
                _logger.LogInformation("Cache was revoked while loading; reading again");
                continue;
            }

            var span = TimeSpan.FromMinutes(10);
            _cache.Set(nameof(Document), cachedDocuments, span);

            _cache.Set(nameof(Subscription), cachedSubscriptions, span);
            _cache.Set(nameof(Notifier), cachedNotifiers, span);
            _cache.Set(nameof(WorkGroup), cachedWorkGroups, span);
            _cache.Set(nameof(GlobalAdapterValuesSet), cachedGlobalValues, span);
            _cache.Set(nameof(BusGateway), cachedBusGateways, span);
            return;
        }
    }

    public async Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId)
    {
        if (!_cache.TryGetValue(nameof(Subscription), out Subscription[] cachedSubscriptions))
        {
            await Load();
            return _cache.Get<Subscription[]>(nameof(Subscription)).Where(sub => sub.DocumentId == documentId).ToArray();
        }

        return cachedSubscriptions.Where(sub => sub.DocumentId == documentId).ToArray();
    }

    public async Task<BusGatewayRoute[]> ListBusGatewayRoutesByDocumentAsync(int documentId)
    {
        if (!_cache.TryGetValue(nameof(BusGateway), out BusGateway[] cachedBusGateways))
        {
            await Load();
            cachedBusGateways = _cache.Get<BusGateway[]>(nameof(BusGateway));
        }

        // A deactivated gateway offers no routes, which is the whole of what deactivating
        // one means on the bus side: the message still publishes, this gateway just stops
        // being one of the places it lands.
        return cachedBusGateways
            .Where(g => g.DocumentId == documentId && !g.Inactive)
            .SelectMany(g => g.Routes ?? Enumerable.Empty<BusGatewayRoute>())
            .ToArray();
    }

    public async Task<Notifier[]> ListNotifiersAsync()
    {
        if (!_cache.TryGetValue(nameof(Notifier), out Notifier[] cachedNotifiers))
        {
            await Load();
            return _cache.Get<Notifier[]>(nameof(Notifier));
            
        }

        return cachedNotifiers;
    }

    public async Task<Subscription> SubscriptionByIdAsync(int subscriptionId)
    {
        if (!_cache.TryGetValue(nameof(Subscription), out Subscription[] cachedSubscriptions))
        {
            await Load();
            return _cache.Get<Subscription[]>(nameof(Subscription)).FirstOrDefault(sub => sub.Id == subscriptionId);
        }

        return cachedSubscriptions.FirstOrDefault(sub => sub.Id == subscriptionId);
    }

    public async Task<Document> DocumentByIdAsync(int documentId)
    {
        if (!_cache.TryGetValue(nameof(Document), out Document[] cachedDocuments))
        {
            await Load();
            return _cache.Get<Document[]>(nameof(Document)).FirstOrDefault(d => d.Id == documentId);
        }

        return cachedDocuments.FirstOrDefault(d => d.Id == documentId);
    }

    public async Task<Document> DocumentByNameAsync(string documentName)
    {
        if (!_cache.TryGetValue(nameof(Document), out Document[] cachedDocuments))
        {
            await Load();
            return _cache.Get<Document[]>(nameof(Document)).FirstOrDefault(d =>
                string.Equals(d.Name, documentName, StringComparison.CurrentCultureIgnoreCase));
        }

        return cachedDocuments.FirstOrDefault(d =>
            string.Equals(d.Name, documentName, StringComparison.CurrentCultureIgnoreCase));
    }

    public async Task BroadcastRevoke()
    {
        // This is a best-effort cache-refresh signal, not part of the write
        // itself — a dozen command handlers across Subscriptions/WorkGroups/
        // BusGateways/Documents call this right after SaveChangesAsync, and an
        // unhandled failure here (e.g. the RabbitMQ connection being down)
        // must not turn an already-successful write into a 500 response.
        try
        {
            using var scope = _ssf.CreateScope();
            var broadcast = scope.ServiceProvider.GetRequiredService<IBroadcast>();
            await broadcast.Broadcast(new RevokeCacheMessage());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast cache revoke; cached reads may stay stale until they expire.");
        }
    }

    public async Task<WorkGroup[]> ListWorkGroupsAsync()
    {
        if (!_cache.TryGetValue(nameof(WorkGroup), out WorkGroup[] cachedWorkGroups))
        {
            await Load();
            return _cache.Get<WorkGroup[]>(nameof(WorkGroup));
        }

        return cachedWorkGroups;
    }

    public async Task<WorkGroup> WorkGroupByIdAsync(int workGroupId)
    {
        if (!_cache.TryGetValue(nameof(WorkGroup), out WorkGroup[] cachedWorkGroups))
        {
            await Load();
            return _cache.Get<WorkGroup[]>(nameof(WorkGroup)).FirstOrDefault(wg => wg.Id == workGroupId);
        }

        return cachedWorkGroups.FirstOrDefault(wg => wg.Id == workGroupId);
    }
    public async Task<WorkGroup> WorkGroupBySubscriptionIdAsync(int subscriptionId)
    {
        var subscription = await SubscriptionByIdAsync(subscriptionId);
        if (subscription?.WorkGroupId == null)
            return null;

        return await WorkGroupByIdAsync(subscription.WorkGroupId.Value);
    }

    public async Task<GlobalAdapterValuesSet> GlobalAdapterValuesSetById(string globalAdapterValuesSetId)
    {
        if (!_cache.TryGetValue(nameof(GlobalAdapterValuesSet), out GlobalAdapterValuesSet[] cachedGlobalValues))
        {
            await Load();
            return _cache.Get<GlobalAdapterValuesSet[]>(nameof(GlobalAdapterValuesSet)).FirstOrDefault(gav => gav.Id == globalAdapterValuesSetId);
        }

        return cachedGlobalValues.FirstOrDefault(gav => gav.Id == globalAdapterValuesSetId);
    }

    public async Task<GlobalAdapterValuesSet[]> ListGlobalAdapterValuesSetsAsync()
    {
        if (!_cache.TryGetValue(nameof(GlobalAdapterValuesSet), out GlobalAdapterValuesSet[] cachedGlobalValues))
        {
            await Load();
            return _cache.Get<GlobalAdapterValuesSet[]>(nameof(GlobalAdapterValuesSet));
        }

        return cachedGlobalValues;
    }

    public void Revoke()
    {
        // Before the removals, so a load that reads after this sees the new value and a load that
        // read before it is discarded. Bumping afterwards would leave a window where a load could
        // both miss the removal and match the generation.
        Interlocked.Increment(ref _generation);

        _cache.Remove(nameof(Subscription));
        _cache.Remove(nameof(Notifier));
        _cache.Remove(nameof(Document));
        _cache.Remove(nameof(WorkGroup));
        _cache.Remove(nameof(BusGateway));
        // Load() caches this one too. Leaving it out here meant no write of any kind could
        // clear a global value: it sat for its full ten minutes regardless.
        _cache.Remove(nameof(GlobalAdapterValuesSet));
    }
}