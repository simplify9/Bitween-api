using Newtonsoft.Json;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Domain
{
    class PublishEvents : IHandle<XchangeCreatedEvent>
    {
        private readonly IPublish publish;

        public PublishEvents(IPublish publish)
        {
            this.publish = publish;
        }

        async public Task Handle(XchangeCreatedEvent domainEvent)
        {
            await publish.Publish(domainEvent.GetType().Name, JsonConvert.SerializeObject(domainEvent));
        }
    }
}
