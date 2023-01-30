using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;

namespace SW.Infolink;

public class InMemoryInfolinkCache : IInfolinkCache
{
    readonly ReaderWriterLockSlim _lock = new();
    DateTime? cachePreparedOn;
    Subscription[] cachedSubscriptions;
    Document[] cachedDocuments;

    readonly IServiceScopeFactory ssf;
    readonly ILogger<InMemoryInfolinkCache> logger;

    public InMemoryInfolinkCache(IServiceScopeFactory ssf, ILogger<InMemoryInfolinkCache> logger)
    {
        this.ssf = ssf ?? throw new ArgumentNullException(nameof(ssf));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    async Task Load()
    {
        using var scope = ssf.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
        cachedSubscriptions = (await repo.ListAsync<Subscription>()).ToArray();
        cachedDocuments = (await repo.ListAsync<Document>()).ToArray();
    }
    
    async Task Ensure()
    {
        _lock.EnterWriteLock();
        try
        {
            if (cachePreparedOn == null || DateTime.UtcNow.Subtract(cachePreparedOn.Value).TotalMinutes > 10)
            {
                await Load();
                cachePreparedOn = DateTime.UtcNow;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    public async Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId)
    {
        await Ensure();
        _lock.EnterReadLock();
        try
        {
            return cachedSubscriptions.Where(sub => sub.DocumentId == documentId).ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task<Document> DocumentByIdAsync(int documentId)
    {
        await Ensure();
            
        _lock.EnterReadLock();
        try
        {
            return cachedDocuments.FirstOrDefault(d => d.Id == documentId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Revoke()
    {
        _lock.EnterWriteLock();
        try
        {
            cachePreparedOn = null;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}