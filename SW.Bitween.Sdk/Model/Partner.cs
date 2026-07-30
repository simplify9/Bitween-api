using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
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
        /// <summary>
        /// The names of the partner's adapter properties — never their values, which
        /// can be secrets. Names alone are enough to count them in a list and to offer
        /// them as {{partner.x}} reference tokens when configuring an adapter.
        /// </summary>
        public ICollection<string> PropertyKeys { get; set; }
    }

    public class PartnerUpdate : PartnerCreate
    {
        public ICollection<KeyAndValue> ApiCredentials { get; set; }
        public ICollection<SubscriptionSearch> Subscriptions { get; set; }
        public Dictionary<string, string> AdapterProperties { get; set; }
    }
}
