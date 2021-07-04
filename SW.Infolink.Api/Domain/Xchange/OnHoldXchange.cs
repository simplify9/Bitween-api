using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class OnHoldXchange:BaseEntity
    {
        private OnHoldXchange(){}
        public OnHoldXchange(Subscription subscription, string data, string fileName = null, bool badData = false, string[] references = null)
        {
            References = references ?? new string[] { };
            SubscriptionId = subscription.Id;
            FileName = fileName;
            Data = data;
            BadData = badData;
        }
        public int SubscriptionId { get; private set; }
        
        public string FileName { get;private set; }

        public string Data { get; private set;}

        public bool BadData { get; private set;}
        public string[] References { get; private set; }
    }
}