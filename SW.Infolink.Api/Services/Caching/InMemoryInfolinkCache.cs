using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;

namespace SW.Infolink;

public class InMemoryInfolinkCache : IInfolinkCache
{
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _ssf;
    private readonly ILogger<InMemoryInfolinkCache> _logger;

    public InMemoryInfolinkCache(IMemoryCache memoryCache, IServiceScopeFactory ssf,
        ILogger<InMemoryInfolinkCache> logger)
    {
        _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _ssf = ssf ?? throw new ArgumentNullException(nameof(ssf));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task Load()
    {
        using var scope = _ssf.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
        _logger.LogInformation("Loading documents and subscriptions to cache");
        var cachedSubscriptions = await repo.Set<Subscription>()
            .AsNoTracking().Where(i => !i.Inactive).ToArrayAsync();
        var cachedDocuments = await repo.Set<Document>().AsNoTracking().ToArrayAsync();
        var cachedNotifiers = await repo.Set<Notifier>().Where(i => !i.Inactive).AsNoTracking().ToArrayAsync();

        var span = TimeSpan.FromMinutes(10);
        _cache.Set("documents", cachedDocuments, span);
        _cache.Set("subscriptions", cachedSubscriptions, span);
        _cache.Set("notifiers", cachedNotifiers, span);
    }

    public async Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId)
    {
        if (!_cache.TryGetValue("subscriptions", out Subscription[] cachedSubscriptions))
        {
            await Load();
            return _cache.Get<Subscription[]>("subscriptions").Where(sub => sub.DocumentId == documentId).ToArray();
        }

        return cachedSubscriptions.Where(sub => sub.DocumentId == documentId).ToArray();
    }

    public async Task<Notifier[]> ListNotifiersAsync()
    {
        if (!_cache.TryGetValue("notifiers", out Notifier[] cachedNotifiers))
        {
            await Load();
            return _cache.Get<Notifier[]>("notifiers");
        }

        return cachedNotifiers;
    }

    public async Task<Subscription> SubscriptionByIdAsync(int subscriptionId)
    {
        if (!_cache.TryGetValue("subscriptions", out Subscription[] cachedSubscriptions))
        {
            await Load();
            return _cache.Get<Subscription[]>("subscriptions").FirstOrDefault(sub => sub.Id == subscriptionId);
        }

        return cachedSubscriptions.FirstOrDefault(sub => sub.Id == subscriptionId);
    }

    public async Task<Document> DocumentByIdAsync(int documentId)
    {
        if (!_cache.TryGetValue("documents", out Document[] cachedDocuments))
        {
            await Load();
            return _cache.Get<Document[]>("documents").FirstOrDefault(d => d.Id == documentId);
        }

        return cachedDocuments.FirstOrDefault(d => d.Id == documentId);
    }

    public void Revoke()
    {
        _cache.Remove("subscriptions");
        _cache.Remove("notifiers");
        _cache.Remove("documents");
    }
}