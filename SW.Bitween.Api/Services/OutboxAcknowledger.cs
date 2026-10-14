using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// Deletes outbox rows the broker has taken, in batches, off the path of the exchange that wrote
/// them.
/// </summary>
/// <remarks>
/// <para>
/// A published row used to be marked with an update straight after its publish and kept for a
/// day: two extra round trips per exchange on the hot path, and a day of dead rows for autovacuum
/// on a busy deployment. Now the publisher hands the ids here and moves on; they are deleted at
/// most a second later, up to <see cref="BatchSize"/> in one statement.
/// </para>
/// <para>
/// Nothing is lost if a batch never runs (a pod killed, the database away): the rows still look
/// unpublished, and <see cref="OutboxDispatcher"/> publishes them again once they are past its
/// grace period. Delivery was already at least once; consumers take a repeat.
/// </para>
/// </remarks>
public sealed class OutboxAcknowledger(IServiceScopeFactory scopes, ILogger<OutboxAcknowledger> logger)
    : BackgroundService
{
    public const int BatchSize = 500;
    static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(1);

    // Bounded, so a database that stays away for long can't grow it without limit. A full queue
    // hands the ids back, and the publisher deletes them itself.
    readonly Channel<long> published = Channel.CreateBounded<long>(
        new BoundedChannelOptions(100_000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    readonly PublishedCount publishedCount = new();

    /// <summary>Messages this node published in the last hour, for the queue health page.</summary>
    public int PublishedLastHour => publishedCount.LastHour();

    /// <summary>
    /// Queues published rows for deletion. False when the queue is full: the caller deletes them.
    /// </summary>
    public bool Acknowledge(IReadOnlyCollection<long> ids)
    {
        publishedCount.Add(ids.Count);
        foreach (var id in ids)
            if (!published.Writer.TryWrite(id))
                return false;
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await published.Reader.WaitToReadAsync(stoppingToken);
                await Task.Delay(FlushEvery, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushAsync(CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Deletes everything queued so far. Public for tests, which can't wait a second.</summary>
    public async Task FlushAsync(CancellationToken cancellation)
    {
        var batch = new List<long>(BatchSize);
        while (true)
        {
            batch.Clear();
            while (batch.Count < BatchSize && published.Reader.TryRead(out var id)) batch.Add(id);
            if (batch.Count == 0) return;

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                await DeleteAsync(db, batch, cancellation);
            }
            catch (Exception ex)
            {
                // The dispatcher publishes these again after its grace period; a repeat is harmless.
                logger.LogWarning(ex, "Couldn't delete {Count} published outbox row(s); they may be published again.",
                    batch.Count);
                return;
            }
        }
    }

    internal static Task<int> DeleteAsync(BitweenDbContext db, IReadOnlyCollection<long> ids,
        CancellationToken cancellation) =>
        db.Set<OutboxMessage>().Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(cancellation);

    /// <summary>Messages published per minute over the last hour.</summary>
    sealed class PublishedCount
    {
        readonly long[] minutes = new long[60];
        readonly long[] counts = new long[60];

        public void Add(int count)
        {
            var minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            var slot = (int)(minute % 60);
            lock (counts)
            {
                if (minutes[slot] != minute)
                {
                    minutes[slot] = minute;
                    counts[slot] = 0;
                }
                counts[slot] += count;
            }
        }

        public int LastHour()
        {
            var since = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute - 59;
            long total = 0;
            lock (counts)
                for (var i = 0; i < 60; i++)
                    if (minutes[i] >= since) total += counts[i];
            return (int)Math.Min(total, int.MaxValue);
        }
    }
}
