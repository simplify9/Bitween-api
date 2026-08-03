using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.EfCoreExtensions;

namespace SW.Bitween.Resources.Ops;

/// <summary>
/// What a consumed queue is for. The bus names queues after the consumer class and the
/// bus message type — <c>xchangeservice.48low-result</c> — and only Bitween knows that
/// <c>48low</c> is work group 48. Resolving it here keeps the answer next to
/// <see cref="WorkGroup.GetBusMessageName"/>, which is the formula's owner; the UI used
/// to rebuild that formula in TypeScript and match on the string.
/// </summary>
public enum QueueLane
{
    /// <summary>One per bus-enabled information type. Inbound messages become exchanges.</summary>
    FrontDoor,

    /// <summary>One per work group. Where integrations run.</summary>
    Work,

    /// <summary>A work group's <c>-Result</c> lane. Evaluates notifiers.</summary>
    Notifications,

    /// <summary>A legacy event type, consumed only while that setting is on.</summary>
    Legacy,

    /// <summary>Bookkeeping. No integration traffic.</summary>
    Control,
}

/// <param name="Lane">What the queue is for.</param>
/// <param name="Title">The name of the thing it belongs to, or the raw message name if that no longer resolves.</param>
/// <param name="WorkGroupId">Set on a work or notifications lane whose group still exists.</param>
/// <param name="InformationTypeId">Set on a front door whose information type still exists.</param>
public record LaneIdentity(QueueLane Lane, string Title, int? WorkGroupId, int? InformationTypeId);

/// <summary>
/// Decodes the message-type half of a queue name into the thing it belongs to. One database
/// read for all rows, so callers should resolve a whole snapshot at once.
/// </summary>
public class LaneResolver(BitweenDbContext dbContext)
{
    /// <summary><see cref="XchangeService"/>'s non-lane message types.</summary>
    private static readonly Dictionary<string, string> Control = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["SubscriptionUnpausedEvent"] = "Resumed integrations",
    };

    /// <summary>Only declared while <c>Bitween.ConsumeLegacyEventMessages</c> is on.</summary>
    private static readonly Dictionary<string, string> Legacy = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["ApiXchangeCreatedEvent"] = "API exchange created",
        ["InternalXchangeCreatedEvent"] = "Internal exchange created",
        ["ReceivingXchangeCreatedEvent"] = "Received exchange created",
        ["AggregateXchangeCreatedEvent"] = "Aggregated exchange created",
        ["XchangeResultCreatedEvent"] = "Exchange result created",
    };

    private List<WorkGroup> workGroups;
    private List<Document> documents;

    public async Task Prepare()
    {
        workGroups = await dbContext.Set<WorkGroup>().AsNoTracking().ToListAsync();
        documents = (await dbContext.ListAsync(new BusEnabledDocuments())).ToList();
    }

    /// <param name="consumerName">The consumer class name, as the bus reports it.</param>
    /// <param name="messageName">The bus message type name.</param>
    public LaneIdentity Resolve(string consumerName, string messageName)
    {
        if (consumerName == nameof(BusService))
        {
            var document = documents.FirstOrDefault(d =>
                string.Equals(d.BusMessageTypeName, messageName, System.StringComparison.OrdinalIgnoreCase));

            // A bus message only gets a queue while its information type is bus-enabled, so a
            // name that resolves to nothing means the queue outlived it — and queues are never
            // deleted. Reported as-is rather than hidden.
            return new LaneIdentity(QueueLane.FrontDoor, document?.Name ?? messageName, null, document?.Id);
        }

        // Tolerate a consumer class this hasn't been taught rather than guessing it is a lane.
        if (consumerName != nameof(XchangeService))
            return new LaneIdentity(QueueLane.Control, $"{consumerName} · {messageName}", null, null);

        if (Control.TryGetValue(messageName, out var control))
            return new LaneIdentity(QueueLane.Control, control, null, null);

        if (Legacy.TryGetValue(messageName, out var legacy))
            return new LaneIdentity(QueueLane.Legacy, legacy, null, null);

        var isResult = messageName.EndsWith(XchangeService.ResultQueueSuffix, System.StringComparison.OrdinalIgnoreCase);
        var lane = isResult ? QueueLane.Notifications : QueueLane.Work;
        var groupName = isResult
            ? messageName[..^XchangeService.ResultQueueSuffix.Length]
            : messageName;

        // WorkGroup.None — id 0, so it can never collide with a real group. This is the lane
        // everything without a work group shares.
        if (string.Equals(groupName, WorkGroup.None.GetBusMessageName(), System.StringComparison.OrdinalIgnoreCase))
            return new LaneIdentity(lane, "Ungrouped", null, null);

        var workGroup = workGroups.FirstOrDefault(wg =>
            string.Equals(wg.GetBusMessageName(), groupName, System.StringComparison.OrdinalIgnoreCase));

        return new LaneIdentity(lane, workGroup?.Name ?? messageName, workGroup?.Id, null);
    }
}
