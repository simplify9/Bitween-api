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
    readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    DateTime? cachePreparedOn;
    Subscription[] cachedSubscriptions = null;
    Document[] cachedDocuments = null;

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

        return cachedSubscriptions.Where(sub => sub.DocumentId == documentId).ToArray();
    }

    public async Task<Document> DocumentByIdAsync(int documentId)
    {
        await Ensure();

        return cachedDocuments.FirstOrDefault(d => d.Id == documentId);
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