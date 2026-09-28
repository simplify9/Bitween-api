using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    public class OnHoldXchange:BaseEntity
    {
        private OnHoldXchange(){}
        public OnHoldXchange(Subscription subscription, string data, string fileName = null, bool badData = false, string[] references = null,
            int? partnerId = null, string correlationId = null)
        {
            References = references ?? new string[] { };
            SubscriptionId = subscription.Id;
            FileName = fileName;
            Data = data;
            BadData = badData;
            PartnerId = partnerId;
            CorrelationId = correlationId;
        }
        public int SubscriptionId { get; private set; }
        
        public string FileName { get;private set; }

        public string Data { get; private set;}

        public bool BadData { get; private set;}
        public string[] References { get; private set; }

        /// <summary>
        /// The partner to run as once released, when it is not the subscription's own — a response
        /// runs as the partner of the subscription that fed it. Null means the subscription's own.
        /// </summary>
        public int? PartnerId { get; private set; }

        public string CorrelationId { get; private set; }
    }
}