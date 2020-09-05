using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Infolink.Domain
{
    public class Subscription : BaseEntity
    {
        private Subscription()
        {
        }

        public Subscription(string name, int documentId, SubscriptionType type, int partnerId) : this(name, documentId, type)
        {
            PartnerId = partnerId;
        }

        public Subscription(string name, int documentId, SubscriptionType type)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DocumentId = documentId;
            Type = type;
            Schedules = new List<Schedule>();
            ReceiveSchedules = new List<Schedule>();
            HandlerProperties = new Dictionary<string, string>();
            MapperProperties = new Dictionary<string, string>();
            ReceiverProperties = new Dictionary<string, string>();
            DocumentFilter = new Dictionary<string, string>();
        }

        public string Name { get; set; }
        public int DocumentId { get; private set; }
        public SubscriptionType Type { get; private set; }
        public int? PartnerId { get; private set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public bool Temporary { get; set; }
        public bool Aggregate { get; set; }

        public IReadOnlyDictionary<string, string> HandlerProperties { get; private set; }
        public IReadOnlyDictionary<string, string> MapperProperties { get; private set; }
        public IReadOnlyDictionary<string, string> ReceiverProperties { get; private set; }
        public IReadOnlyDictionary<string, string> DocumentFilter { get; private set; }

        public void SetDictionaries(
            IReadOnlyDictionary<string, string> handler, 
            IReadOnlyDictionary<string, string> mapper, 
            IReadOnlyDictionary<string, string> receiver, 
            IReadOnlyDictionary<string, string> document)
        {
            HandlerProperties = handler;
            MapperProperties = mapper;
            ReceiverProperties = receiver;
            DocumentFilter = document;
        }

        public bool Inactive { get; set; }
        public ICollection<Schedule> Schedules { get; private set; }
        public int? ResponseSubscriptionId { get; set; }
        public ICollection<Schedule> ReceiveSchedules { get; private set; }
        public string ReceiverId { get; set; }
        public DateTime? ReceiveOn { get; set; }
    }
}
