using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
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
