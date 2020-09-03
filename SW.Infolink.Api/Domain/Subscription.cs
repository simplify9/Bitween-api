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

        public Subscription(string name, int documentId)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DocumentId = documentId;
            Schedules = new List<Schedule>();
            Receivers = new List<Receiver>();
            HandlerProperties = new Dictionary<string, string>();
            MapperProperties = new Dictionary<string, string>();
            DocumentFilter = new Dictionary<string, string>();
        }

        public string Name { get; set; }
        public int DocumentId { get; private set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public bool Temporary { get; set; }
        public bool Aggregate { get; set; }
        public IReadOnlyDictionary<string, string> HandlerProperties { get; set; }
        public IReadOnlyDictionary<string, string> MapperProperties { get; set; }
        public IReadOnlyDictionary<string, string> DocumentFilter { get; set; }
        public bool Inactive { get; set; }
        public ICollection<Schedule> Schedules { get; private set; }
        public ICollection<Receiver> Receivers { get; private set; }
        public int? ResponseSubscriptionId { get; set; }

    }
}
