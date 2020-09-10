using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{

    public class PartnerCreate  : IName
    {
        public string Name { get; set; }

    }
    public class PartnerRow : PartnerUpdate
    {
        public int Id { get; set; }
    }

    public class PartnerUpdate : PartnerCreate
    {
        public ICollection<KeyAndValue> ApiCredentials { get; set; }
        public ICollection<SubscriptionSearch> Subscriptions { get; set; }
    }
}
