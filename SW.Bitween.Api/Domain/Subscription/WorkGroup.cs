using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

public class WorkGroup : BaseEntity
{
    public string Name { get; set; }
    public string BusMessageName { get; set; }
    public static WorkGroup None => new() { BusMessageName = "Ungrouped"};
}