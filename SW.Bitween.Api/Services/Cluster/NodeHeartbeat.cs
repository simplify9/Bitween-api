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
        await db.SaveChangesAsync(cancellation);

        var forgotten = now - ForgetAfter;
        await db.Set<ClusterNode>().Where(n => n.LastSeenOn < forgotten).ExecuteDeleteAsync(cancellation);
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
