using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Metrics;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// Adds what this node counted into the hourly rows, once a minute and once more on the way down.
/// Per table that is one read of this node's rows for the hours in hand and one save: the cost of
/// metrics to the database is a few small writes a minute per node, however busy the node is.
/// </summary>
public class MetricsFlusher(MetricsRecorder recorder, IServiceScopeFactory scopes, ILogger<MetricsFlusher> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
            await FlushAsync(CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Writes what is counted so far. Public for tests, which can't wait a minute.</summary>
    public async Task FlushAsync(CancellationToken cancellation)
    {
        var snapshot = recorder.Drain();
        if (snapshot.IsEmpty) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await AddExchangesAsync(db, snapshot, cancellation);
            await AddAdaptersAsync(db, snapshot, cancellation);
            await AddErrorsAsync(db, snapshot, cancellation);
            await db.SaveChangesAsync(cancellation);
        }
        catch (Exception ex)
        {
            // Kept for the next flush rather than lost: a database blip costs a minute's delay.
            recorder.Restore(snapshot);
            logger.LogWarning(ex, "Couldn't write this node's metrics; they are kept for the next try.");
        }
    }

    static async Task AddExchangesAsync(BitweenDbContext db, MetricsRecorder.Snapshot snapshot, CancellationToken cancellation)
    {
        if (snapshot.Exchanges.Count == 0) return;
        var node = MetricsRecorder.Node;
        var hours = snapshot.Exchanges.Select(s => s.Hour).Distinct().ToList();
        var existing = (await db.Set<ExchangeStats>().Where(r => r.Node == node && hours.Contains(r.Hour)).ToListAsync(cancellation))
            .ToDictionary(r => (r.Hour, r.SubscriptionId, r.PartnerId));
        foreach (var counted in snapshot.Exchanges)
            if (existing.TryGetValue((counted.Hour, counted.SubscriptionId, counted.PartnerId), out var row)) row.Add(counted);
            else db.Add(counted);
    }

    static async Task AddAdaptersAsync(BitweenDbContext db, MetricsRecorder.Snapshot snapshot, CancellationToken cancellation)
    {
        if (snapshot.Adapters.Count == 0) return;
        var node = MetricsRecorder.Node;
        var hours = snapshot.Adapters.Select(s => s.Hour).Distinct().ToList();
        var existing = (await db.Set<AdapterStats>().Where(r => r.Node == node && hours.Contains(r.Hour)).ToListAsync(cancellation))
            .ToDictionary(r => (r.Hour, r.AdapterId, r.Version, r.Role, r.Runtime));
        foreach (var counted in snapshot.Adapters)
            if (existing.TryGetValue((counted.Hour, counted.AdapterId, counted.Version, counted.Role, counted.Runtime), out var row)) row.Add(counted);
            else db.Add(counted);
    }

    static async Task AddErrorsAsync(BitweenDbContext db, MetricsRecorder.Snapshot snapshot, CancellationToken cancellation)
    {
        if (snapshot.Errors.Count == 0) return;
        var node = MetricsRecorder.Node;
        var hours = snapshot.Errors.Select(s => s.Hour).Distinct().ToList();
        var existing = (await db.Set<ErrorStats>().Where(r => r.Node == node && hours.Contains(r.Hour)).ToListAsync(cancellation))
            .ToDictionary(r => (r.Hour, r.Scope, r.ScopeKey, r.Fingerprint));
        foreach (var counted in snapshot.Errors)
            if (existing.TryGetValue((counted.Hour, counted.Scope, counted.ScopeKey, counted.Fingerprint), out var row))
            {
                row.Count += counted.Count;
                row.Sample = counted.Sample;
                if (counted.LastSeenOn > row.LastSeenOn) row.LastSeenOn = counted.LastSeenOn;
            }
            else db.Add(counted);
    }
}
