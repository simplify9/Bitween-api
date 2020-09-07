using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    internal class XchangeCreatedEvent : BaseDomainEvent
    {
        public string Id { get; set; }
    }
}
