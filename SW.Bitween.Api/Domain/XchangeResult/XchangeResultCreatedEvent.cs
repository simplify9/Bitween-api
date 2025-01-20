using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    public class XchangeResultCreatedEvent : BaseDomainEvent
    {
        public string Id { get; set; }
        public bool Success { get; set; }
        public bool ResponseBad { get; set; }
    }
}
