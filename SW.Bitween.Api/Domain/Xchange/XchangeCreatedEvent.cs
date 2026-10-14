using Newtonsoft.Json;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    internal class XchangeMessage
    {
        public string Id { get; set; }

        /// <summary>
        /// The exchange's input, as written to storage, when it is no larger than
        /// <c>Bitween:MessagePayloadMaxBytes</c>: the worker uses it instead of reading storage. Only on
        /// the publish straight after the commit; the outbox row stays the id alone, so a message the
        /// dispatcher publishes again has none, and the worker reads storage, as it always did.
        /// Storage stays the record: this is a copy for the trip. Left out when there is none, so a
        /// stored body is exactly what it always was.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Input { get; set; }

        /// <summary>
        /// The node holding the synchronous gateway call that waits for this exchange's result
        /// (<see cref="ResultSignals.Node"/>), which the worker tells once the result is saved. Set by
        /// the gateway's sync calls only, on the publish straight after the commit; a message without
        /// it is answered by the call's backstop poll.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AwaitedOn { get; set; }
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
