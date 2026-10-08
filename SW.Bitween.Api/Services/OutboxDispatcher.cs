using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// Publishes the outbox rows that were not published straight after their commit — because the
/// broker was down, or because they were saved inside a caller's transaction — and clears out old
/// published ones.
/// </summary>
/// <remarks>
/// Every replica runs one. A row is claimed with a conditional update before it is published, so
/// two replicas never publish the same row at the same time; a claim expires, so a replica that
/// dies holding one does not strand it.
/// </remarks>
public class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);

    // Left alone this long, so the publish straight after the commit gets its chance first.
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeepPublished = TimeSpan.FromDays(1);
    private const int BatchSize = 100;

    private DateTime _nextCleanup = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await DispatchOnceAsync(stoppingToken);
                if (published > 0)
                    logger.LogInformation("Published {Count} message(s) left in the outbox.", published);

                if (DateTime.UtcNow >= _nextCleanup)
                {
                    await CleanUpAsync(stoppingToken);
                    _nextCleanup = DateTime.UtcNow.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The outbox dispatcher failed a pass; it will try again.");
            }

            try { await Task.Delay(PollEvery, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <returns>How many were published.</returns>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var now = DateTime.UtcNow;
        var cutoff = now - Grace;
        var candidates = await db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.PublishedOn == null && m.CreatedOn < cutoff &&
                        (m.ClaimedUntil == null || m.ClaimedUntil < now))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var claimed = new System.Collections.Generic.List<OutboxMessage>();
        foreach (var message in candidates)
        {
            var until = DateTime.UtcNow + ClaimFor;
            var claimTime = DateTime.UtcNow;
            var won = await db.Set<OutboxMessage>()
                .Where(m => m.Id == message.Id && m.PublishedOn == null &&
                            (m.ClaimedUntil == null || m.ClaimedUntil < claimTime))
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ClaimedUntil, until), cancellationToken);
            if (won == 1) claimed.Add(message);
        }

        if (claimed.Count == 0) return 0;

        var published = await db.PublishOutboxAsync(claimed, cancellationToken);
        if (published < claimed.Count)
            logger.LogWarning("{Count} outbox message(s) still could not be published; retrying shortly.",
                claimed.Count - published);

        return published;
    }

    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var before = DateTime.UtcNow - KeepPublished;

        int deleted;
        do
        {
            var batch = await db.Set<OutboxMessage>()
                .Where(m => m.PublishedOn != null && m.PublishedOn < before)
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .Take(1000)
                .ToListAsync(cancellationToken);
            deleted = batch.Count == 0
                ? 0
                : await db.Set<OutboxMessage>().Where(m => batch.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
        } while (deleted > 0 && !cancellationToken.IsCancellationRequested);
    }
}
