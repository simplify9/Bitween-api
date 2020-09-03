using Newtonsoft.Json;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Domain
{
    class PublishEvents : IHandle<BaseDomainEvent>
    {
        private readonly IPublish publish;

        public PublishEvents(IPublish publish)
        {
            this.publish = publish;
        }

        async public Task Handle(BaseDomainEvent domainEvent)
        {
            await publish.Publish(domainEvent.GetType().Name, JsonConvert.SerializeObject(domainEvent));
        }
    }
}
