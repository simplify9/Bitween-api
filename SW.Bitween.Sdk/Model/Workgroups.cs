using System;

namespace SW.Bitween.Model;
public class ConsumerSettings
{
    public ushort? Prefetch { get; set; }
    public int? Priority { get; set; }
}

public class WorkGroupOptions
{
    public ConsumerSettings RabbitMqOptions { get; set; }
}

public class WorkGroupModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string BusMessageName { get; set; }
    public WorkGroupOptions Options { get; set; }
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
}

public class CreateWorkGroupModel
{
    public string Name { get; set; }
    public string BusMessageName { get; set; }
    public WorkGroupOptions Options { get; set; }
}

public class SearchWorkGroupModel
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public class UpdateWorkGroupModel : CreateWorkGroupModel
{
}

public class DeleteWorkGroupModel
{
}