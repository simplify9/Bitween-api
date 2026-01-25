using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
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


    public InMemoryBitweenCache(IMemoryCache memoryCache, IServiceScopeFactory ssf,
        ILogger<InMemoryBitweenCache> logger)
    {
        _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _ssf = ssf ?? throw new ArgumentNullException(nameof(ssf));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task Load()
    {
        using var scope = _ssf.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        _logger.LogInformation("Loading documents and subscriptions to cache");
        var cachedSubscriptions = await repo.Set<Subscription>().Include(s=>s.WorkGroup)
            .AsNoTracking().Where(i => !i.Inactive).ToArrayAsync();
        var cachedDocuments = await repo.Set<Document>().AsNoTracking().ToArrayAsync();
        var cachedNotifiers = await repo.Set<Notifier>().Where(i => !i.Inactive).AsNoTracking().ToArrayAsync();
        var cachedWorkGroups = await repo.Set<WorkGroup>().AsNoTracking().ToArrayAsync();
        var span = TimeSpan.FromMinutes(10);
        _cache.Set(nameof(Document), cachedDocuments, span);
        
        _cache.Set(nameof(Subscription), cachedSubscriptions, span);
        _cache.Set(nameof(Notifier), cachedNotifiers, span);
        _cache.Set(nameof(WorkGroup), cachedWorkGroups, span);
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

    public Task BroadcastRevoke()
    {
        using var scope = _ssf.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<IBroadcast>();
        return broadcast.Broadcast(new RevokeCacheMessage());
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

    public void Revoke()
    {
        _cache.Remove(nameof(Subscription));
        _cache.Remove(nameof(Notifier));
        _cache.Remove(nameof(Document));
        _cache.Remove(nameof(WorkGroup));
    }
}