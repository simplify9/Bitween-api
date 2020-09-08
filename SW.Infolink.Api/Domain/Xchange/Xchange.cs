using SW.Infolink.Api;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Infolink.Domain
{
    public class Xchange : BaseEntity<string>
    {
        private Xchange()
        {
        }

        public Xchange(int documentId, XchangeFile file, string[] references = null)
        {
            Id = Guid.NewGuid().ToString("N"); 
            DocumentId = documentId;
            References = references;
            InputName = file.Filename;
            InputSize = file.Data.Length;
            InputHash = file.Hash;
            StartedOn = DateTime.UtcNow;
            Events.Add(new XchangeCreatedEvent
            {
                Id = Id
            });
        }

        public Xchange(Subscription subscription, XchangeFile file, string[] references = null, bool ignoreSchedule = false) : this(subscription.DocumentId, file, references)
        {
            SubscriptionId = subscription.Id;
            MapperId = subscription.MapperId;
            HandlerId = subscription.HandlerId;
            MapperProperties = subscription.MapperProperties;
            HandlerProperties = subscription.HandlerProperties;
            ResponseSubscriptionId = subscription.ResponseSubscriptionId;

            if (!ignoreSchedule)
                DeliverOn = subscription?.Schedules.Next() ;
        }

        public int? SubscriptionId { get; private set; }
        public int DocumentId { get; private set; }
        public string HandlerId { get; private set; }
        public string MapperId { get; private set; }
        public IReadOnlyDictionary<string, string> HandlerProperties { get; private set; }
        public IReadOnlyDictionary<string, string> MapperProperties { get; private set; }
        public string[] References { get; private set; }
        public DateTime StartedOn { get; private set; }
        public string InputName { get; private set; }
        public int InputSize { get; private set; }
        public string InputHash { get; private set; }
        public DateTime? DeliverOn { get; private set; }
        public int? ResponseSubscriptionId { get; private set; }
    }
}
