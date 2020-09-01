using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Domain
{
    class PublishXchangeSubmittedEvent : IHandle<XchangeSubmittedEvent>
    {
        private readonly IPublish publish;

        public PublishXchangeSubmittedEvent(IPublish publish)
        {
            this.publish = publish;
        }

        async public Task Handle(XchangeSubmittedEvent domainEvent)
        {
            var message = new XchangeSubmittedMessage
            {
                XchangeId = domainEvent.Xchange.Id,
                HandlerId = domainEvent.Xchange.HandlerId,
                MapperId = domainEvent.Xchange.MapperId,
                InputFileName = domainEvent.Xchange.InputFileName,
                SubscriberId = domainEvent.Subscriber.Id,
                SubscriberProperties = domainEvent.Subscriber.Properties.ToDictionary()

            };

            await publish.Publish(message);

        }
    }
}
