using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

public class DeleteUnattendedQueuesModel
{
    /// <summary>Lanes' main queues, as <see cref="UnattendedQueueView.QueueName"/> reports them.</summary>
    public List<string> QueueNames { get; set; } = [];
}

public record UnattendedQueueSkip(string QueueName, string Reason);

public record DeleteUnattendedQueuesResult(List<string> Deleted, List<UnattendedQueueSkip> Skipped);

/// <summary>
/// Deletes unattended lanes — each one's main queue and whichever of retry/bad exist — along with
/// whatever they hold. Names are only ever matched against a fresh unattended list, never passed to
/// the broker as given, so this can't be pointed at a queue something still uses. A lane that can't
/// go is skipped with the reason rather than failing the rest.
/// </summary>
[HandlerName("DeleteUnattendedQueues")]
public class DeleteUnattendedQueues(BrokerQueues brokerQueues,
    ILogger<DeleteUnattendedQueues> logger,
    BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<DeleteUnattendedQueuesModel, object>
{
    public async Task<object> Handle(DeleteUnattendedQueuesModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.Operate);

        var lanes = (await brokerQueues.FindUnattendedLanes(fresh: true))
            .ToDictionary(l => l.Key, StringComparer.OrdinalIgnoreCase);

        var deleted = new List<string>();
        var skipped = new List<UnattendedQueueSkip>();
        foreach (var name in (request.QueueNames ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!lanes.TryGetValue(name, out var lane))
            {
                skipped.Add(new(name, "It isn't an unattended queue, or it's already gone."));
                continue;
            }

            if (lane.Any(q => q.Consumers > 0))
            {
                skipped.Add(new(name, "Something is still reading it."));
                continue;
            }

            try
            {
                // No waiting: a reader that attached since the list was read keeps its lane.
                if ((await brokerQueues.DeleteLanes([lane.Key], TimeSpan.Zero)).Count > 0)
                {
                    skipped.Add(new(name, "Something is still reading it."));
                    continue;
                }
                deleted.Add(lane.Key);
                logger.LogInformation("Account {AccountId} deleted unattended queue lane {Lane} ({Messages} messages)",
                    requestContext.GetNameIdentifier(), lane.Key, lane.Sum(q => q.Messages));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Deleting unattended queue lane {Lane} failed", lane.Key);
                skipped.Add(new(name, "RabbitMQ refused to delete it."));
            }
        }

        return new DeleteUnattendedQueuesResult(deleted, skipped);
    }
}
