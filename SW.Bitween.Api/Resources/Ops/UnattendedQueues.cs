using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EasyNetQ.Management.Client;
using EasyNetQ.Management.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bus;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

/// <param name="QueueName">The main queue as RabbitMQ has it.</param>
/// <param name="Messages">Messages sitting in the main queue.</param>
/// <param name="RetryMessages">Messages in its <c>.retry</c> queue, if it has one.</param>
/// <param name="DeadMessages">Messages in its <c>.bad</c> queue, if it has one.</param>
/// <param name="Queues">How many queues this lane is (main plus whichever of retry/bad exist).</param>
public record UnattendedQueueView(
    string QueueName,
    long Messages,
    long RetryMessages,
    long DeadMessages,
    int Queues);

/// <summary>
/// Queues that exist in RabbitMQ under this instance's prefix that nothing here consumes.
/// <para>
/// Every other Ops endpoint derives its list from the consumer definitions of the running
/// process, so it can only ever show queues this instance already knows about. Deleting or
/// renaming a work group leaves its queues behind — the bus never removes a queue — and they
/// vanish from those endpoints while keeping whatever they still hold. This is the only view
/// that asks the broker instead of asking ourselves.
/// </para>
/// </summary>
[HandlerName("UnattendedQueues")]
public class UnattendedQueues(IBusDashboardDataService dashboardDataService,
    BusOptions busOptions,
    IMemoryCache memoryCache,
    BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View, Model.Permissions.Dashboard.View);

        var prefix = string.IsNullOrWhiteSpace(busOptions.ApplicationName)
            ? busOptions.ProcessExchange
            : $"{busOptions.ProcessExchange}.{busOptions.ApplicationName}";

        // Same three names per consumer the bus itself declares.
        var health = await dashboardDataService.GetConsumerHealthAsync();
        var attended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var consumer in health)
        {
            attended.Add(consumer.QueueName);
            attended.Add($"{consumer.QueueName}.retry");
            attended.Add($"{consumer.QueueName}.bad");
        }

        // Cached on the same clock as the bus's own management call, because this page polls.
        var queues = await memoryCache.GetOrCreateAsync("bitween-all-queues", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(busOptions.MonitoringCacheSeconds);
            try
            {
                var client = new ManagementClient(new Uri(busOptions.ManagementUrl),
                    busOptions.ManagementUsername, busOptions.ManagementPassword);
                return await client.GetQueuesAsync(busOptions.VirtualHost);
            }
            catch
            {
                // Management API unreachable or misconfigured - degrade to "no data" instead of 500ing.
                return Array.Empty<Queue>();
            }
        });

        var orphans = queues
            .Where(q => q.Name.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase))
            .Where(q => !attended.Contains(q.Name))
            // One node queue per running process, named with a fresh guid each start, so old
            // ones pile up by design and are not a signal worth reporting.
            .Where(q => !q.Name.StartsWith($"{prefix}.node", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Reported per lane, not per queue: a lane is three queues, and listing them separately
        // triples a list that is already long enough to bury the ones holding messages.
        return orphans
            .GroupBy(q => Regex.Replace(q.Name, @"\.(retry|bad)$", "", RegexOptions.IgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            .Select(lane => new UnattendedQueueView(
                lane.Key,
                lane.Where(q => !IsRetry(q.Name) && !IsBad(q.Name)).Sum(q => q.Messages),
                lane.Where(q => IsRetry(q.Name)).Sum(q => q.Messages),
                lane.Where(q => IsBad(q.Name)).Sum(q => q.Messages),
                lane.Count()))
            .OrderByDescending(l => l.Messages + l.RetryMessages + l.DeadMessages)
            .ThenBy(l => l.QueueName)
            .ToArray();
    }

    private static bool IsRetry(string name) => name.EndsWith(".retry", StringComparison.OrdinalIgnoreCase);
    private static bool IsBad(string name) => name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase);
}
