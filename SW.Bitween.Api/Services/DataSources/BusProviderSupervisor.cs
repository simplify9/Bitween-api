using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Cluster;
using SW.Serverless.Resident;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Reconciles running adapters against the data sources this node should be serving.
///
/// Desired state is the set of active <see cref="DataSource"/> rows; actual state is what the
/// resident host is running. The loop starts what is missing, stops what is no longer wanted, and
/// restarts what has changed — the same shape as the Quartz schedule reconciliation that already
/// exists for subscriptions.
///
/// PLACEMENT. A broker connection is exclusive — two nodes consuming the same queue is duplicate
/// processing, which is the failure this whole design exists to prevent. So every data source is
/// owned through a lease, granted per data source rather than globally: whichever node wins each
/// race owns that source, so load spreads without anyone scheduling it.
///
/// A lease is checked, not assumed. Before every reconcile the supervisor revalidates what it
/// believes it owns, because holding the lock is not the same as still being the current owner —
/// a node paused long enough for its queue to be released and reclaimed would otherwise carry on
/// consuming. Losing a lease stops its adapter immediately.
/// </summary>
public class BusProviderSupervisor(IServiceProvider serviceProvider, IResidentAdapterHost adapters,
    ILeaderElection election, ILogger<BusProviderSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);

    // What we last started, and the configuration fingerprint it was started with.
    private readonly Dictionary<int, string> _running = new();

    // What this node currently owns. Nothing runs without an entry here.
    private readonly Dictionary<int, IResourceLease> _leases = new();

    /// <summary>
    /// Consecutive failed starts per data source on this node, and when this node may next try to
    /// lease one it gave up. A node that cannot reach a broker kept the lease and retried forever,
    /// so the replica that could reach it never got the chance.
    /// </summary>
    private readonly Dictionary<int, int> _startFailures = new();
    private readonly Dictionary<int, DateTime> _yieldedUntil = new();
    private const int StartFailuresBeforeYielding = 3;
    private static readonly TimeSpan YieldFor = TimeSpan.FromMinutes(5);

    private static string ResourceOf(DataSource dataSource) => $"datasource.{dataSource.Id}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let one bad reconcile end the loop; the next pass retries everything.
                logger.LogError(ex, "Bus provider reconciliation failed.");
            }

            try { await Task.Delay(ReconcileInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Release on the way out so a rolling restart hands ownership over in seconds rather than
        // leaving the next node to wait for the broker to time the connection out.
        foreach (var dataSourceId in _leases.Keys.ToList())
            await ReleaseAsync(dataSourceId);
    }

    /// <summary>
    /// One reconciliation pass. Public because "reconcile now" is a real operation — the same
    /// shape as the receivenow endpoint for subscriptions — and because a supervisor whose only
    /// entry point is a thirty-second timer cannot be tested at all.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var desired = await dbContext.Set<DataSource>()
            .Where(d => !d.Inactive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Endpoint configuration lives on the gateways, so an adapter is told what to consume by
        // the gateways pointing at it rather than by the data source alone.
        var endpoints = await dbContext.Set<BusGateway>()
            .Where(g => g.DataSourceId != null && !g.Inactive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Composed into the Statements value the adapter has always received. The adapter contract
        // did not change when these moved out of a JSON field on the data source and into rows —
        // it still resolves a name and knows nothing about where the SQL is kept.
        var statements = await dbContext.Set<DataSourceStatement>()
            .Where(s => !s.Inactive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Revalidate first. A lease that is no longer current must stop its adapter before
        // anything else happens this pass, not after.
        await ReleaseLostLeasesAsync(cancellationToken);

        foreach (var dataSource in desired)
        {
            if (!_leases.ContainsKey(dataSource.Id) &&
                _yieldedUntil.TryGetValue(dataSource.Id, out var yieldedUntil) && yieldedUntil > DateTime.UtcNow)
                continue;

            if (!await EnsureOwnedAsync(dataSource, cancellationToken))
            {
                // Owned by another node. If we were running it, we are not any more.
                await StopIfRunningAsync(dataSource.Id, dataSource.AdapterId, cancellationToken);
                continue;
            }

            var startupValues = BuildStartupValues(dataSource, endpoints, statements);
            var fingerprint = Fingerprint(dataSource, startupValues);

            if (_running.TryGetValue(dataSource.Id, out var current))
            {
                if (current == fingerprint) continue;

                logger.LogInformation("Data source {Name} changed; restarting its adapter.", dataSource.Name);
                await adapters.StopAsync(dataSource.AdapterId, dataSource.Id.ToString(),
                    drain: true, cancellationToken);
                _running.Remove(dataSource.Id);
            }

            try
            {
                await adapters.StartExclusiveAsync(new AdapterSpec
                {
                    AdapterId = dataSource.AdapterId,
                    // The instance key IS the data source id, which is how the sink knows which
                    // gateway an inbound message belongs to.
                    InstanceKey = dataSource.Id.ToString(),
                    StartupValues = startupValues,

                    // Zero leaves the host's own default in place; the spec only overrides when
                    // an operator has actually chosen a ceiling for this connection.
                    SoftMemoryLimitBytes = Megabytes(dataSource.SoftMemoryLimitMb),
                    HardMemoryLimitBytes = Megabytes(dataSource.HardMemoryLimitMb),
                    CpuPercentLimit = dataSource.CpuPercentLimit,
                    CpuLimitSamples = dataSource.CpuLimitSamples
                }, cancellationToken);

                _running[dataSource.Id] = fingerprint;
                _startFailures.Remove(dataSource.Id);
                _yieldedUntil.Remove(dataSource.Id);
                logger.LogInformation("Data source {Name} running on adapter {AdapterId}.",
                    dataSource.Name, dataSource.AdapterId);
            }
            catch (Exception ex)
            {
                // One unreachable broker must not stop the others from starting.
                logger.LogError(ex, "Could not start adapter {AdapterId} for data source {Name}.",
                    dataSource.AdapterId, dataSource.Name);

                // And the operator who configured it learns why here rather than from the node's
                // log. A start that throws leaves no instance to describe, so nothing else in this
                // class would ever record it.
                await RecordStartFailureAsync(dbContext, dataSource, ex, cancellationToken);

                // An exclusive source is held by one node at a time. If this node keeps failing to
                // start it — it can't reach the broker, say — let it go for a while so another
                // replica can try; a single-node deployment just retries after the pause.
                var failures = _startFailures[dataSource.Id] = _startFailures.GetValueOrDefault(dataSource.Id) + 1;
                if (failures >= StartFailuresBeforeYielding && _leases.ContainsKey(dataSource.Id))
                {
                    logger.LogWarning(
                        "Data source {Name} failed to start {Failures} times on this node; releasing its lease for {Minutes} minutes so another node can take it.",
                        dataSource.Name, failures, YieldFor.TotalMinutes);
                    await ReleaseAsync(dataSource.Id);
                    _startFailures.Remove(dataSource.Id);
                    _yieldedUntil[dataSource.Id] = DateTime.UtcNow + YieldFor;
                }
            }
        }

        // Anything running that is no longer desired. Its lease goes too — holding a lock on a
        // data source nobody wants would block a node that later does.
        var wanted = desired.Select(d => d.Id).ToHashSet();
        foreach (var id in _running.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            await StopIfRunningAsync(id, adapterId: null, cancellationToken);
            await ReleaseAsync(id);
        }

        await WriteBackHealthAsync(dbContext, cancellationToken);
    }

    /// <summary>
    /// True when this node may run the data source. Acquires the lease if it does not hold one, and
    /// revalidates the term if it does.
    ///
    /// A PER-NODE source skips all of that, and must: a database connection pool is not a thing one
    /// node holds on everyone else's behalf. Leasing one would leave every other replica unable to
    /// run the Xchanges that need it, reporting "not running here" as though that were the normal
    /// answer it is for a broker.
    /// </summary>
    private async Task<bool> EnsureOwnedAsync(DataSource dataSource, CancellationToken cancellationToken)
    {
        if (dataSource.Resolve() == DataSourcePlacement.PerNode)
        {
            // Defensive: a source whose placement changed from Exclusive must not keep the lease it
            // no longer needs, or the next node to want it as exclusive would wait forever.
            if (_leases.ContainsKey(dataSource.Id)) await ReleaseAsync(dataSource.Id);
            return true;
        }

        if (_leases.TryGetValue(dataSource.Id, out var held))
        {
            if (await held.ValidateAsync(cancellationToken)) return true;

            logger.LogWarning(
                "Lease on data source {Name} is no longer current; another node has taken it.",
                dataSource.Name);

            await ReleaseAsync(dataSource.Id);
            return false;
        }

        var lease = await election.TryAcquireAsync(ResourceOf(dataSource), cancellationToken);
        if (lease == null) return false;

        _leases[dataSource.Id] = lease;
        return true;
    }

    private async Task ReleaseLostLeasesAsync(CancellationToken cancellationToken)
    {
        foreach (var (dataSourceId, lease) in _leases.ToList())
        {
            if (await lease.ValidateAsync(cancellationToken)) continue;

            logger.LogWarning("Lost the lease on data source {DataSourceId} at term {Term}; stopping it.",
                dataSourceId, lease.Term);

            // Stopped WITHOUT draining: another node may already be consuming, so finishing
            // in-flight work here risks processing the same messages twice.
            await StopIfRunningAsync(dataSourceId, adapterId: null, cancellationToken, drain: false);
            await ReleaseAsync(dataSourceId);
        }
    }

    private async Task StopIfRunningAsync(int dataSourceId, string adapterId,
        CancellationToken cancellationToken, bool drain = true)
    {
        if (!_running.ContainsKey(dataSourceId)) return;

        adapterId ??= adapters.Describe()
            .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString())?.AdapterId;

        if (adapterId != null)
            await adapters.StopAsync(adapterId, dataSourceId.ToString(), drain, cancellationToken);

        _running.Remove(dataSourceId);
    }

    private async Task ReleaseAsync(int dataSourceId)
    {
        if (!_leases.Remove(dataSourceId, out var lease)) return;
        await lease.DisposeAsync();
    }

    /// <summary>
    /// Connection settings from the data source, plus the endpoints its gateways want consumed.
    /// The adapter decides what to do with them — Bitween does not model any broker's topology.
    /// </summary>
    private static Dictionary<string, string> BuildStartupValues(DataSource dataSource,
        IEnumerable<BusGateway> gateways, IEnumerable<DataSourceStatement> statements)
    {
        var values = new Dictionary<string, string>(dataSource.Properties ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        var mine = gateways.Where(g => g.DataSourceId == dataSource.Id).ToList();

        var wanted = mine.Where(g => !string.IsNullOrWhiteSpace(g.Endpoint))
            .Select(g => g.Endpoint)
            .Distinct()
            .ToList();

        if (wanted.Count > 0) values["Endpoints"] = string.Join(",", wanted);

        // Per-gateway overrides, namespaced so they cannot collide with connection settings.
        // See BusGateway.EndpointProperties: no bundled adapter reads these yet. Skipping the
        // catch-all gateway is deliberate — an override with no endpoint to attach to would land
        // under the meaningless key "Endpoint::something".
        foreach (var gateway in mine.Where(g => !string.IsNullOrWhiteSpace(g.Endpoint)))
            foreach (var kv in gateway.EndpointProperties ?? new())
                values[$"Endpoint:{gateway.Endpoint}:{kv.Key}"] = kv.Value;

        // Composed LAST so it wins over any legacy Statements property left on the data source.
        // Rows are the source of truth now; a stale property silently overriding them would be a
        // very hard afternoon.
        var composed = StatementComposer.Compose(statements, dataSource.Id);
        if (composed != null) values["Statements"] = composed;

        return values;
    }

    private static long Megabytes(int megabytes) =>
        megabytes > 0 ? (long)megabytes * 1024 * 1024 : 0;

    /// <summary>
    /// What the adapter was started WITH. The memory ceilings belong in here as much as the
    /// connection settings do: they are applied to the process at launch, so raising one has no
    /// effect until the adapter restarts — and the fingerprint is what decides that it should.
    /// </summary>
    private static string Fingerprint(DataSource dataSource, Dictionary<string, string> startupValues) =>
        dataSource.AdapterId + "|" +
        dataSource.SoftMemoryLimitMb + "|" + dataSource.HardMemoryLimitMb + "|" +
        dataSource.CpuPercentLimit + "|" + dataSource.CpuLimitSamples + "|" +
        string.Join(";", startupValues.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>
    /// Writes why a start failed onto the data source itself.
    ///
    /// A throw from StartExclusiveAsync leaves nothing running to describe, so the health
    /// write-back below never sees it — which is how a data source ends up showing no connection
    /// and no reason at the same time. The message is the one the operator would otherwise have
    /// had to find in this node's log.
    /// </summary>
    private async Task RecordStartFailureAsync(BitweenDbContext dbContext, DataSource dataSource,
        Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var row = await dbContext.Set<DataSource>()
                .FirstOrDefaultAsync(d => d.Id == dataSource.Id, cancellationToken);
            if (row == null) return;

            row.LastKnownState = "Failed";
            row.LastException = AdapterFailureReader.Explain(exception.Message,
                adapters.Get(dataSource.AdapterId, dataSource.Id.ToString())?.Diagnostics);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Recording why something failed must not become a second thing that fails.
            logger.LogWarning(ex, "Could not record the start failure for data source {Name}.",
                dataSource.Name);
        }
    }

    /// <summary>
    /// Health from the heartbeat, written back so the UI and the notifiers can see a broker that
    /// has gone away without anyone tailing logs.
    /// </summary>
    private async Task WriteBackHealthAsync(BitweenDbContext dbContext, CancellationToken cancellationToken)
    {
        // Only the instances that ARE a data source. Describe() answers for everything this host
        // holds, and a POOLED instance is keyed by its pool slot rather than by a data source —
        // several can share one key, which made a plain ToDictionary throw "an item with the same
        // key has already been added" and take the whole reconcile pass down with it.
        //
        // Pooled bus instances became routine when a delivery on a node that does not own the
        // connection started opening a send-only one. They have no health to write back: nothing
        // owns them, and the row they would write to belongs to the node that does.
        var health = adapters.Describe()
            .Where(h => int.TryParse(h.InstanceKey, out var id) && id > 0)
            .GroupBy(h => h.InstanceKey)
            .ToDictionary(g => g.Key, g => g.First());

        if (health.Count == 0) return;

        var ids = health.Keys.Select(int.Parse).ToList();
        var rows = await dbContext.Set<DataSource>()
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken);

        var changed = false;

        foreach (var row in rows)
        {
            if (!health.TryGetValue(row.Id.ToString(), out var instance)) continue;

            row.LastKnownState = instance.ReportedState ?? instance.State.ToString();
            row.LastHeartbeatOn = instance.LastHeartbeatOn?.UtcDateTime;

            // LastError is what a LIVE adapter says about itself, so it is null for exactly the
            // failure an operator most needs explained: one that died on startup and never got as
            // far as reporting anything. The process printed why before it exited, so fall back to
            // that — otherwise the screen shows "Quarantined" with no reason anywhere near it.
            row.LastException = instance.LastError
                                ?? AdapterFailureReader.Summarise(
                                    adapters.Get(row.AdapterId, row.Id.ToString())?.Diagnostics);
            row.ConsecutiveFailures = instance.RestartCount;

            // A per-node source has no owner and saying "null" would read as nobody running it.
            // Its row is written by every node, last one wins — which is honest enough for a
            // summary, and the Telemetry endpoint answers per node for anything more precise.
            row.OwnedByNode = row.Resolve() == DataSourcePlacement.PerNode
                ? "every node"
                : _leases.TryGetValue(row.Id, out var lease)
                    ? $"{(election as RabbitMqLeaderElection)?.NodeName ?? Environment.MachineName} (term {lease.Term})"
                    : null;
            changed = true;
        }

        if (changed) await dbContext.SaveChangesAsync(cancellationToken);
    }
}
