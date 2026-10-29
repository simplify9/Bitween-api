using System;

namespace SW.Bitween.Model;
public class ConsumerSettings
{
    public ushort? Prefetch { get; set; }
    public int? Priority { get; set; }

    /// <summary>
    /// Parallel exchanges: how many exchanges of the work group's lane run at once on each node.
    /// 1 runs them one at a time in the order they arrive, so a partner gets one call at a time per
    /// node. Null takes the prefetch. Prefetch caps it.
    /// </summary>
    public ushort? Concurrency { get; set; }
}

public class WorkGroupOptions
{
    public ConsumerSettings? RabbitMqOptions { get; set; }
}

public class WorkGroupModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string BusMessageName { get; set; } = null!;
    public WorkGroupOptions? Options { get; set; }
    public double? ProcessorAckRate { get; set; }
    public double? ProcessorIncomingRate { get; set; }
    public long? ProcessorProcessingCount { get; set; }
    public long? ProcessorQueueCount { get; set; }
    public double? NotifierAckRate { get; set; }
    public double? NotifierIncomingRate { get; set; }
    public long? NotifierProcessingCount { get; set; }
    public long? NotifierQueueCount { get; set; }
    /// <summary>Live count of active RabbitMQ consumer instances for this group's queue.</summary>
    public long? ProcessorNodeCount { get; set; }

    /// <summary>
    /// How many subscriptions run in this work group. Counted here because the admin UI shows it
    /// in the list: computing it client-side meant downloading every subscription alongside every
    /// page of this list.
    /// </summary>
    public int UsedByCount { get; set; }
}

public class CreateWorkGroupModel
{
    /// <summary>Both required; the server rejects a create without them.</summary>
    public string Name { get; set; } = null!;

    public string BusMessageName { get; set; } = null!;

    /// <summary>Optional; defaults apply when absent.</summary>
    public WorkGroupOptions? Options { get; set; }
}

public class SearchWorkGroupModel
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? Name { get; set; }
}

public class UpdateWorkGroupModel : CreateWorkGroupModel
{
}

public class DeleteWorkGroupModel
{
}