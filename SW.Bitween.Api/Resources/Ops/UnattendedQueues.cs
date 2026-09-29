using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

/// <param name="QueueName">The main queue as RabbitMQ has it.</param>
/// <param name="Messages">Messages sitting in the main queue.</param>
/// <param name="RetryMessages">Messages in its <c>.retry</c> queue, if it has one.</param>
/// <param name="DeadMessages">Messages in its <c>.bad</c> queue, if it has one.</param>
/// <param name="Queues">How many queues this lane is (main plus whichever of retry/bad exist).</param>
/// <param name="Consumers">
/// Consumers the broker reports across the lane's queues, from any instance. Above zero means
/// something still reads it — a listener that outlived its work group, or another instance
/// running different code during a deploy — so it isn't safe to delete.
/// </param>
/// <param name="InformationTypeId">
/// Set when the lane is the queue of an information type paused on the bus: the type keeps its
/// name and its queue, but has no consumer while paused, so its queue is listed here.
/// </param>
public record UnattendedQueueView(
    string QueueName,
    long Messages,
    long RetryMessages,
    long DeadMessages,
    int Queues,
    long Consumers,
    int? InformationTypeId);

/// <summary>
/// Queues that exist in RabbitMQ under this instance's prefix that nothing here consumes.
/// <para>
/// Every other Ops endpoint derives its list from the consumer definitions of the running
/// process, so it can only ever show queues this instance already knows about. A paused
/// information type's queue is one of these; so is anything left behind before deletes and
/// renames took their queues with them, or recreated empty by a client reconnecting — and they
/// vanish from those endpoints while keeping whatever they still hold. This is the only view
/// that asks the broker instead of asking ourselves.
/// </para>
/// </summary>
[HandlerName("UnattendedQueues")]
public class UnattendedQueues(BrokerQueues brokerQueues,
    BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View, Model.Permissions.Dashboard.View);

        var lanes = await brokerQueues.FindUnattendedLanes();

        var pausedTypes = (await dbContext.Set<Document>().AsNoTracking()
                .Where(d => !d.BusEnabled && d.BusMessageTypeName != null && d.BusMessageTypeName != "")
                .Select(d => new { d.Id, d.BusMessageTypeName })
                .ToListAsync())
            .GroupBy(d => brokerQueues.InformationTypeQueue(d.BusMessageTypeName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        return lanes
            .Select(lane => new UnattendedQueueView(
                lane.Key,
                lane.Where(q => !BrokerQueues.IsRetry(q.Name) && !BrokerQueues.IsBad(q.Name)).Sum(q => q.Messages),
                lane.Where(q => BrokerQueues.IsRetry(q.Name)).Sum(q => q.Messages),
                lane.Where(q => BrokerQueues.IsBad(q.Name)).Sum(q => q.Messages),
                lane.Count(),
                lane.Sum(q => (long)q.Consumers),
                pausedTypes.TryGetValue(lane.Key, out var typeId) ? typeId : null))
            .OrderByDescending(l => l.Messages + l.RetryMessages + l.DeadMessages)
            .ThenBy(l => l.QueueName)
            .ToArray();
    }
}
