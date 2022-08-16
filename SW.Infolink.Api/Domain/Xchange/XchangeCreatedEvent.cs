using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    internal abstract class XchangeCreatedEvent : BaseDomainEvent
    {
        public string Id { get; set; }

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
