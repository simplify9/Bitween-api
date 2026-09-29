using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EasyNetQ.Management.Client;
using EasyNetQ.Management.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SW.Bus;
using SW.Bus.RabbitMqExtensions;

namespace SW.Bitween.Resources.Ops;

/// <summary>
/// Bitween's queues as the broker has them, through the RabbitMQ management API: the ones
/// nothing reads any more, and deleting a lane — a main queue with its retry and bad queues.
/// </summary>
public class BrokerQueues(IBusDashboardDataService dashboardDataService,
    BusOptions busOptions,
    IMemoryCache memoryCache,
    ILogger<BrokerQueues> logger) : IDisposable
{
    private const string CacheKey = "bitween-all-queues";

    /// <summary>How long a work group or information type change waits for every instance to let go of its old queues.</summary>
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(10);

    // One per request: a batch delete makes a call per queue, and a client each would be a
    // connection each.
    private ManagementClient client;
    private ManagementClient Client => client ??= new ManagementClient(new Uri(busOptions.ManagementUrl),
        busOptions.ManagementUsername, busOptions.ManagementPassword);

    private string Prefix => string.IsNullOrWhiteSpace(busOptions.ApplicationName)
        ? busOptions.ProcessExchange
        : $"{busOptions.ProcessExchange}.{busOptions.ApplicationName}";

    /// <summary>
    /// Queues under this instance's prefix that no consumer definition declares, grouped into
    /// lanes keyed by the main queue's name. <paramref name="fresh"/> skips the cache, for a
    /// caller about to act on the answer rather than display it.
    /// </summary>
    public async Task<List<IGrouping<string, Queue>>> FindUnattendedLanes(bool fresh = false)
    {
        // Same three names per consumer the bus itself declares.
        var health = await dashboardDataService.GetConsumerHealthAsync();
        var attended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var consumer in health)
        {
            attended.Add(consumer.QueueName);
            attended.Add($"{consumer.QueueName}.retry");
            attended.Add($"{consumer.QueueName}.bad");
        }

        var queues = fresh ? await FetchQueues() : await CachedQueues();

        // Reported per lane, not per queue: a lane is three queues, and listing them separately
        // triples a list that is already long enough to bury the ones holding messages.
        return queues
            .Where(q => q.Name.StartsWith($"{Prefix}.", StringComparison.OrdinalIgnoreCase))
            .Where(q => !attended.Contains(q.Name))
            // One node queue per running process, named with a fresh guid each start, so old
            // ones pile up by design and are not a signal worth reporting.
            .Where(q => !q.Name.StartsWith($"{Prefix}.node", StringComparison.OrdinalIgnoreCase))
            .GroupBy(q => Regex.Replace(q.Name, @"\.(retry|bad)$", "", RegexOptions.IgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>An information type's front-door queue, as the bus names it from <see cref="BusService"/>'s message types.</summary>
    public string InformationTypeQueue(string busMessageTypeName) =>
        $"{Prefix}.{nameof(BusService)}.{busMessageTypeName}".ToLower();

    /// <summary>
    /// Deletes a work group's two lanes after the group is deleted or its bus message name
    /// changes. Never throws: see <see cref="DeleteReleased"/>.
    /// </summary>
    /// <param name="busMessageName">The group's <see cref="Domain.WorkGroup.GetBusMessageName"/> before the change.</param>
    public Task DeleteWorkGroupLanes(string busMessageName)
    {
        // The same names the bus derives from XchangeService's message types.
        var main = $"{Prefix}.{nameof(XchangeService)}.{busMessageName}".ToLower();
        return DeleteReleased([main, $"{main}{XchangeService.ResultQueueSuffix}".ToLower()]);
    }

    /// <summary>
    /// Deletes an information type's queue after the type is deleted or renamed on the bus.
    /// Never throws: see <see cref="DeleteReleased"/>.
    /// </summary>
    public Task DeleteInformationTypeLane(string busMessageTypeName) =>
        DeleteReleased([InformationTypeQueue(busMessageTypeName)]);

    /// <summary>
    /// The change that made these lanes obsolete is already saved, so a broker that can't be
    /// reached leaves them for the Queue health page rather than failing the request.
    /// </summary>
    private async Task DeleteReleased(string[] mainQueues)
    {
        try
        {
            var held = await DeleteLanes(mainQueues, ReleaseWait);
            if (held.Count > 0)
                logger.LogWarning("Still read after {Seconds}s, left for Queue health: {Lanes}",
                    ReleaseWait.TotalSeconds, string.Join(", ", held));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't delete the queues {Lanes}", string.Join(", ", mainQueues));
        }
    }

    /// <summary>
    /// Deletes each lane once nothing reads it, waiting up to <paramref name="wait"/> in all. The
    /// bus lets go of a removed consumer shortly after the refresh broadcast, on every instance, so
    /// this retries the broker's own if-unused delete rather than trusting the management stats,
    /// which lag by seconds. A lane already gone counts as deleted.
    /// </summary>
    /// <returns>The lanes still read when time ran out, left untouched.</returns>
    public async Task<List<string>> DeleteLanes(IEnumerable<string> mainQueues, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        var held = new List<string>();
        foreach (var main in mainQueues)
        {
            // Main first: it's the only one with a consumer, so once it goes nothing is left to
            // dead-letter into the retry and bad queues.
            var released = await TryDelete(main);
            while (!released && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                released = await TryDelete(main);
            }

            if (!released)
            {
                held.Add(main);
                continue;
            }

            await TryDelete($"{main}.retry");
            await TryDelete($"{main}.bad");
        }

        memoryCache.Remove(CacheKey);
        return held;
    }

    /// <returns>False while something still consumes it; true once it's gone, whoever removed it.</returns>
    private async Task<bool> TryDelete(string queueName)
    {
        try
        {
            await Client.DeleteQueueAsync(busOptions.VirtualHost, queueName,
                new DeleteQueueCriteria(ifEmpty: false, ifUnused: true));
            return true;
        }
        catch (UnexpectedHttpStatusCodeException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }
        // The if-unused refusal. The broker answers it as a 400 whose reason ends "in use", which
        // the message carries; any other 400 is a real failure, not something to wait out.
        catch (UnexpectedHttpStatusCodeException ex) when (ex.StatusCode == HttpStatusCode.BadRequest
            && ex.Message.Contains(" in use", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    // Cached on the same clock as the bus's own management call, because the page polls.
    // A failed fetch is deliberately not cached: caching it would report "no queues" for the
    // rest of the cache window even after the management API recovers.
    private async Task<IReadOnlyList<Queue>> CachedQueues()
    {
        if (memoryCache.TryGetValue(CacheKey, out IReadOnlyList<Queue> queues)) return queues;
        try
        {
            queues = await FetchQueues();
            memoryCache.Set(CacheKey, queues, TimeSpan.FromSeconds(busOptions.MonitoringCacheSeconds));
            return queues;
        }
        catch
        {
            // Management API unreachable or misconfigured - degrade to "no data" instead of 500ing.
            return Array.Empty<Queue>();
        }
    }

    private async Task<IReadOnlyList<Queue>> FetchQueues() =>
        await Client.GetQueuesAsync(busOptions.VirtualHost);

    public void Dispose() => client?.Dispose();

    internal static bool IsRetry(string name) => name.EndsWith(".retry", StringComparison.OrdinalIgnoreCase);
    internal static bool IsBad(string name) => name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase);
}
