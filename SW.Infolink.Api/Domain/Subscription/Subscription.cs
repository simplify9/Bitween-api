using SW.EfCoreExtensions;
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

        //receiving
        public Subscription(string name, int documentId) : this(name, documentId, SubscriptionType.Receiving)
        { 
        }

        //aggregation
        public Subscription(string name, int aggregationFor, int partnerId) : this(name, Document.AggregationDocumentId, SubscriptionType.Aggregation, partnerId, aggregationFor)
        {
        }

        //apiresult or filter
        public Subscription(string name, int documentId, SubscriptionType type, int partnerId) : this(name, documentId, type, partnerId, null)
        {
            if (!(type == SubscriptionType.ApiCall || type == SubscriptionType.FilterResult))
                throw new ArgumentException();
        }

        private Subscription(string name, int documentId, SubscriptionType type, int? partnerId = null, int? aggregationFor =null,  bool temporary = false)
        {
            PartnerId = partnerId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DocumentId = documentId;
            Type = type;
            Schedules = new List<Schedule>();
            _ReceiveSchedules = new HashSet<Schedule>();
            HandlerProperties = new Dictionary<string, string>();
            MapperProperties = new Dictionary<string, string>();
            ReceiverProperties = new Dictionary<string, string>();
            ValidatorProperties = new Dictionary<string, string>();
            DocumentFilter = new Dictionary<string, string>();
            Temporary = temporary;
        }

        public string Name { get; set; }
        public int DocumentId { get; private set; }
        public SubscriptionType Type { get; private set; }
        public int? PartnerId { get; private set; }
        public bool Temporary { get; private set; }

        public string ValidatorId { get; set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        //public bool Aggregate { get; set; }

        public IReadOnlyDictionary<string, string> ValidatorProperties { get; private set; }
        public IReadOnlyDictionary<string, string> HandlerProperties { get; private set; }
        public IReadOnlyDictionary<string, string> MapperProperties { get; private set; }
        public IReadOnlyDictionary<string, string> ReceiverProperties { get; private set; }
        public IReadOnlyDictionary<string, string> DocumentFilter { get; private set; }

        public void SetDictionaries(
            IReadOnlyDictionary<string, string> handler,
            IReadOnlyDictionary<string, string> mapper,
            IReadOnlyDictionary<string, string> receiver,
            IReadOnlyDictionary<string, string> document,
            IReadOnlyDictionary<string, string> validator
            )
        {
            HandlerProperties = handler;
            MapperProperties = mapper;
            ReceiverProperties = receiver;
            ValidatorProperties = validator;
            DocumentFilter = document;

        }

        public bool Inactive { get; set; }
        public ICollection<Schedule> Schedules { get; private set; }
        public int? ResponseSubscriptionId { get; set; }
        public int? AggregationForId { get; private set; }

        public string ReceiverId { get; set; }

        readonly HashSet<Schedule> _ReceiveSchedules;
        public IReadOnlyCollection<Schedule> ReceiveSchedules => _ReceiveSchedules;
        public void SetReceiveSchedules(IEnumerable<Schedule> schedules = null)
        {
            if (Type == SubscriptionType.Receiving)
            {
                if (schedules != null) _ReceiveSchedules.Update(schedules);
                ReceiveOn = _ReceiveSchedules.Next() ?? throw new InfolinkException("Invalid schedule.");
            }
        }

        public DateTime? ReceiveOn { get; private set; }

        public void SetReceiveResult(string exception = null)
        {
            if (exception == null)
            {
                ReceiveConsecutiveFailures = 0;
                ReceiveLastException = null;
                return;
            }

            ReceiveConsecutiveFailures += 1;
            ReceiveLastException = exception;
        }

        public int ReceiveConsecutiveFailures { get; private set; }
        public string ReceiveLastException { get; private set; }
    }
}
