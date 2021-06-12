using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class XchangeResultCreatedEvent : BaseDomainEvent
    {
        public string Id { get; set; }
        public bool Success { get; set; }
        public bool ResponseBad { get; set; }
    }
}
