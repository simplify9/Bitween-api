using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;

namespace SW.Infolink;

public class InMemoryInfolinkCache : IInfolinkCache
{
    readonly IMemoryCache _cache;
    readonly IServiceScopeFactory ssf;
    readonly ILogger<InMemoryInfolinkCache> logger;

    public InMemoryInfolinkCache(IMemoryCache memoryCache, IServiceScopeFactory ssf, ILogger<InMemoryInfolinkCache> logger)
    {
        _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        this.ssf = ssf ?? throw new ArgumentNullException(nameof(ssf));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    async Task Load()
    {
        using var scope = ssf.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
        logger.LogInformation("Loading documents and subscriptions to cache");
        var cachedSubscriptions = (await repo.ListAsync<Subscription>()).ToArray();
        var cachedDocuments = (await repo.ListAsync<Document>()).ToArray();

        _cache.Set("subscriptions", cachedSubscriptions, TimeSpan.FromMinutes(10));
        _cache.Set("documents", cachedDocuments, TimeSpan.FromMinutes(10));
    }
    
    public async Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId)
    {
        if (!_cache.TryGetValue("subscriptions", out Subscription[] cachedSubscriptions))
        {
            await Load();
        }

        cachedSubscriptions = _cache.Get<Subscription[]>("subscriptions");
        return cachedSubscriptions.Where(sub => sub.DocumentId == documentId).ToArray();
    }

    public async Task<Document> DocumentByIdAsync(int documentId)
    {
        if (!_cache.TryGetValue("documents", out Document[] cachedDocuments))
        {
            await Load();
        }

        cachedDocuments = _cache.Get<Document[]>("documents");
        return cachedDocuments.FirstOrDefault(d => d.Id == documentId);
    }

    public void Revoke()
    {
        _cache.Remove("subscriptions");
        _cache.Remove("documents");
    }
}
