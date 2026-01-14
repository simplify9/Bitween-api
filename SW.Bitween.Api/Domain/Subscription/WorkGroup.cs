using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

public class WorkGroupOptions
{
    public ConsumerOptions RabbitMqOptions { get; set; }
}
public class WorkGroup : BaseEntity
{
    public string Name { get; set; }
    public string BusMessageName { get; set; }
    public static WorkGroup None => new() { BusMessageName = "Ungrouped"};
    public WorkGroupOptions Options { get; set; }
}