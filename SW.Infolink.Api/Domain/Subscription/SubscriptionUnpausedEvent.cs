using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class SubscriptionUnpausedEvent : BaseDomainEvent
    {
        public int Id { get; set; }
    }
}