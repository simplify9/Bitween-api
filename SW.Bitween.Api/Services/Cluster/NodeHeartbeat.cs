using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Cluster;
using SW.Serverless.Runtimes;

namespace SW.Bitween.Services.Cluster;

/// <summary>
/// Writes this node into ClusterNodes every <see cref="Interval"/>, so any node can say which
/// nodes there are, which run data sources and which can run which adapters. A node not seen for
/// <see cref="GoneAfter"/> is shown as gone; one not seen for a week is forgotten.
/// </summary>
public class NodeHeartbeat(IServiceScopeFactory scopes, BitweenOptions options, IServiceProvider services,
    ILogger<NodeHeartbeat> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GoneAfter = TimeSpan.FromSeconds(90);
    static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(7);

    /// <summary>This node's name, as leases record their owner: host and process id.</summary>
    public static string NodeName { get; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    static readonly DateTime StartedOn = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string runtimes = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                runtimes ??= await RuntimesAsync();
                await BeatAsync(runtimes, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The node works without it; only the Nodes view is the poorer for a missed beat.
                logger.LogWarning(ex, "Couldn't record this node's heartbeat.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task BeatAsync(string runtimes, CancellationToken cancellation)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var now = DateTime.UtcNow;

        var node = await db.Set<ClusterNode>().FindAsync([NodeName], cancellation);
        if (node == null) db.Add(node = new ClusterNode(NodeName));
        node.Host = Environment.MachineName;
        node.StartedOn = StartedOn;
        node.LastSeenOn = now;
        node.Version = BitweenInfo.Version(options).ToString();
        node.DataSources = options.BusProvidersEnabled;
        node.Runtimes = runtimes;
        node.ResidentHealth = ResidentHealthJson();
        node.OutboxPublishedLastHour = services.GetService<OutboxAcknowledger>()?.PublishedLastHour;
        await db.SaveChangesAsync(cancellation);

        var forgotten = now - ForgetAfter;
        await db.Set<ClusterNode>().Where(n => n.LastSeenOn < forgotten).ExecuteDeleteAsync(cancellation);
    }

    /// <summary>
    /// The resident adapter instances on this node, as JSON for the Nodes page: what each is doing,
    /// what it uses, how often it has been restarted. Null without any. Kept within the column by
    /// listing at most <see cref="MaxInstances"/>, busiest first.
    /// </summary>
    internal const int MaxInstances = 25;

    string ResidentHealthJson()
    {
        try
        {
            var host = services.GetService<SW.Serverless.Resident.IResidentAdapterHost>();
            var instances = host?.Describe();
            if (instances == null || instances.Count == 0) return null;
            var rows = instances
                .OrderByDescending(i => i.InFlight).ThenByDescending(i => i.WorkingSetBytes)
                .Take(MaxInstances)
                .Select(i => new
                {
                    a = i.AdapterId,
                    s = i.State.ToString(),
                    pid = i.ProcessId,
                    mb = (int)(i.WorkingSetBytes / 1024 / 1024),
                    cpu = Math.Round(i.CpuPercent, 1),
                    up = (long)i.Uptime.TotalSeconds,
                    r = i.RestartCount,
                    q = i.Quarantined,
                    f = i.InFlight,
                    e = i.LastError?.Length > 120 ? i.LastError[..120] : i.LastError,
                });
            var json = System.Text.Json.JsonSerializer.Serialize(rows);
            return json.Length <= 4000 ? json : null;
        }
        catch
        {
            return null;
        }
    }

    async Task<string> RuntimesAsync()
    {
        var detector = services.GetService<AdapterRuntimes>();
        if (detector == null) return "";
        var available = await Task.WhenAll(new[] { "dotnet", "python", "node" }
            .Select(async name => (await detector.StatusAsync(name)).Available ? name : null));
        return string.Join(",", available.Where(n => n != null));
    }
}
