using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    public class XchangeResultCreatedEvent : BaseDomainEvent,IHasWorkGroup
    {
        public string Id { get; set; }
        public bool Success { get; set; }
        public bool ResponseBad { get; set; }
        public IWorkGroup WorkGroup { get; set; } = Domain.WorkGroup.None;
        public string GetBusMessageName()=>  $"{WorkGroup.GetBusMessageName()}{XchangeService.ResultQueueSuffix}";
    }
}
