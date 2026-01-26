using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    internal class XchangeMessage
    {
        public string Id { get; set; }
    }
    internal abstract class XchangeCreatedEvent : BaseDomainEvent,IHasWorkGroup
    {
        public string Id { get; set; }
        public string GetBusMessageName()=> WorkGroup.GetBusMessageName();

        public IWorkGroup WorkGroup { get; set; }

    }
    internal class ApiXchangeCreatedEvent : XchangeCreatedEvent
    {
    }

    internal class ReceivingXchangeCreatedEvent : XchangeCreatedEvent
    {
    }

    internal class InternalXchangeCreatedEvent : XchangeCreatedEvent
    {
    }

    internal class AggregateXchangeCreatedEvent : XchangeCreatedEvent
    {
    }
}
