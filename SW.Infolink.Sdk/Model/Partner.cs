using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Infolink.Model
{

    public class PartnerCreate  : IName
    {
        public string Name { get; set; }
    }
    public class PartnerRow : PartnerUpdate
    {
        public int Id { get; set; }
        public int? SubscriptionsCount { get; set; }
        public int? Keys { get; set; }
        
    }

    public class PartnerUpdate : PartnerCreate
    {
        public ICollection<KeyAndValue> ApiCredentials { get; set; }
        public ICollection<SubscriptionSearch> Subscriptions { get; set; }
    }
}
