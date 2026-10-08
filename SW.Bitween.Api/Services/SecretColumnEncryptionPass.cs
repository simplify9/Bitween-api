using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;

namespace SW.Bitween;

/// <summary>
/// Rewrites the configuration rows saved before their credential columns were encrypted, so the
/// passwords already stored are encrypted too, not only the ones saved from now on.
/// </summary>
/// <remarks>
/// <para>
/// Waits a few minutes after startup first. During a rolling upgrade the previous version is still
/// running, and it cannot read an encrypted column; by then it has normally stopped.
/// </para>
/// <para>
/// Configuration only. Exchanges keep a copy of their subscription's properties, and new ones are
/// encrypted as they are created; those already stored age out with exchange retention rather than
/// rewriting the largest table in the database.
/// </para>
/// </remarks>
public class SecretColumnEncryptionPass(IServiceScopeFactory scopes, ILogger<SecretColumnEncryptionPass> logger)
    : BackgroundService
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!SecretColumnCipher.Enabled)
        {
            logger.LogWarning(
                "Bitween:SettingsEncryptionKey is not set, so partner, adapter and data source credentials " +
                "are stored unencrypted. Set it to encrypt them.");
            return;
        }

        try
        {
            await Task.Delay(Delay, stoppingToken);
            var rewritten = await RunAsync(stoppingToken);
            if (rewritten > 0)
                logger.LogInformation("Re-saved {Count} configuration row(s) so their credentials are encrypted.", rewritten);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Encrypting stored credentials failed; it will be tried again on the next start.");
        }
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        total += await RewriteAsync<Partner>(cancellationToken, nameof(Partner.AdapterProperties));
        total += await RewriteAsync<GlobalAdapterValuesSet>(cancellationToken, nameof(GlobalAdapterValuesSet.Values));
        total += await RewriteAsync<DataSource>(cancellationToken, nameof(DataSource.Properties));
        total += await RewriteAsync<Subscription>(cancellationToken,
            nameof(Subscription.HandlerProperties), nameof(Subscription.MapperProperties),
            nameof(Subscription.ReceiverProperties), nameof(Subscription.ValidatorProperties));
        total += await RewriteAsync<Notifier>(cancellationToken, nameof(Notifier.HandlerProperties));
        total += await RewriteAsync<RetryPolicy>(cancellationToken, "AlertHandlerProperties");
        total += await RewriteAsync<RetryAlertOverride>(cancellationToken, "AlertHandlerProperties");
        return total;
    }

    private async Task<int> RewriteAsync<T>(CancellationToken cancellationToken, params string[] properties)
        where T : class
    {
        const int batchSize = 100;
        var rewritten = 0;
        for (var skip = 0; ; skip += batchSize)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            // Ordered by key, so the batches walk the table once.
            var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
            var batch = await db.Set<T>()
                .OrderBy(e => EF.Property<object>(e, key))
                .Skip(skip).Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) return rewritten;

            foreach (var entity in batch)
                foreach (var property in properties)
                    db.Entry(entity).Property(property).IsModified = true;

            await db.SaveRewrittenValuesAsync(cancellationToken);
            rewritten += batch.Count;
        }
    }
}
