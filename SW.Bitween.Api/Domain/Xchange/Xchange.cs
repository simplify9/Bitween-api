using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using SW.Bitween.Model;
using System.Linq;

namespace SW.Bitween.Domain
{
    public class Xchange : BaseEntity<string>
    {
        private Xchange()
        {
        }

        public Xchange(int documentId, IWorkGroup workGroup, XchangeFile file, string[] references = null,
            SubscriptionType subscriptionType = SubscriptionType.Internal, string correlationId = null)
        {
            Id = Guid.NewGuid().ToString("N");
            DocumentId = documentId;
            References = references ?? new string[] { };
            InputName = file.Filename;
            InputSize = file.Data.Length;
            InputHash = file.Hash;
            InputContentType = file.ContentType;
            StartedOn = DateTime.UtcNow;
            CorrelationId = correlationId;

            XchangeCreatedEvent xchangeEvent = subscriptionType switch
            {
                //break;
                SubscriptionType.Internal => new InternalXchangeCreatedEvent(),
                SubscriptionType.ApiCall => new ApiXchangeCreatedEvent(),
                SubscriptionType.GatewayApiCall => new ApiXchangeCreatedEvent(),
                // Bus-gateway xchanges are bus-triggered async processing, like Internal.
                SubscriptionType.BusGateway => new InternalXchangeCreatedEvent(),
                SubscriptionType.Receiving => new ReceivingXchangeCreatedEvent(),
                SubscriptionType.Aggregation => new AggregateXchangeCreatedEvent(),
                _ => throw new ArgumentOutOfRangeException(nameof(subscriptionType), subscriptionType, null)
            };

            xchangeEvent.Id = Id;
            xchangeEvent.WorkGroup = workGroup ?? WorkGroup.None;
            Events.Add(xchangeEvent);
        }

        public Xchange(Subscription subscription, XchangeFile file, string[] references = null,
            string correlationId = null, Partner gatewayPartner = null, GlobalAdapterValuesSet[] globalAdapterValuesSets = null) :
            this(subscription.DocumentId, subscription.WorkGroup, file, references, subscription.Type)
        {
            SubscriptionId = subscription.Id;
            MapperId = subscription.MapperId;
            HandlerId = subscription.HandlerId;
            ResponseSubscriptionId = subscription.ResponseSubscriptionId;
            ResponseMessageTypeName = subscription.ResponseMessageTypeName;
            PartnerId = gatewayPartner?.Id ?? subscription.PartnerId;
            MapperProperties = (subscription.MapperProperties ?? new Dictionary<string, string>()).ToDictionary().Fill(gatewayPartner, globalAdapterValuesSets);
            HandlerProperties = (subscription.HandlerProperties ?? new Dictionary<string, string>()).ToDictionary()
                .Fill(gatewayPartner, globalAdapterValuesSets);
            CorrelationId = correlationId;
        }

        //retry xchange
        public Xchange(Xchange xchange, XchangeFile file, IWorkGroup workGroup, bool manualRetry = false) :
            this(xchange.DocumentId, workGroup, file, xchange.References)
        {
            ManualRetry = manualRetry;
            SubscriptionId = xchange.SubscriptionId;
            PartnerId = xchange.PartnerId;
            MapperId = xchange.MapperId;
            HandlerId = xchange.HandlerId;
            MapperProperties = xchange.MapperProperties;
            HandlerProperties = xchange.HandlerProperties;
            ResponseSubscriptionId = xchange.ResponseSubscriptionId;
            RetryFor = xchange.Id;
            CorrelationId = xchange.CorrelationId;
        }

        //retry with reset subscription properties
        public Xchange(Subscription subscription, Xchange xchange, XchangeFile file, Partner gatewayPartner = null,
            GlobalAdapterValuesSet[] globalAdapterValuesSets = null, IReadOnlyDictionary<string, int> groupAttemptCounts = null,
            bool manualRetry = false) :
            this(xchange.DocumentId, subscription.WorkGroup, file, xchange.References)
        {
            ManualRetry = manualRetry;
            SubscriptionId = xchange.SubscriptionId;
            PartnerId = xchange.PartnerId ?? subscription.PartnerId;
            MapperId = subscription.MapperId;
            HandlerId = subscription.HandlerId;
            MapperProperties = (subscription.MapperProperties ?? new Dictionary<string, string>()).ToDictionary().Fill(gatewayPartner, globalAdapterValuesSets);
            HandlerProperties = (subscription.HandlerProperties ?? new Dictionary<string, string>()).ToDictionary().Fill(gatewayPartner, globalAdapterValuesSets);
            ResponseSubscriptionId = subscription.ResponseSubscriptionId;
            RetryFor = xchange.Id;
            CorrelationId = xchange.CorrelationId;
        }

        public int? SubscriptionId { get; private set; }
        public int? PartnerId { get; private set; }
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
        public string InputContentType { get; private set; }
        public int? ResponseSubscriptionId { get; private set; }
        public string ResponseMessageTypeName { get; private set; }

        public string RetryFor { get; private set; }

        /// <summary>
        /// <c>true</c> when a person asked for this retry, rather than the retry policy scheduling
        /// it. The policy leaves these alone, so pressing Retry never spends the group's shared
        /// budget and never quietly starts an automatic chain behind the person who pressed it.
        /// </summary>
        public bool ManualRetry { get; private set; }

        public string CorrelationId { get; set; }
    }
}