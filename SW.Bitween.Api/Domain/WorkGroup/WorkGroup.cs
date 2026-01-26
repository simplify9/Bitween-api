using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

public interface IWorkGroup
{
    string BusMessageName { get; }
    string GetBusMessageName();
    WorkGroupOptions Options { get; }
}

public class WorkGroup : BaseEntity,IWorkGroup
{
    public string Name { get; set; }
    public string BusMessageName { get; set; }

    public string GetBusMessageName() => $"{Id}{BusMessageName}";
    //public string 
    public static WorkGroup None => new() { BusMessageName = "Ungrouped"};
    public WorkGroupOptions Options { get; set; }
}