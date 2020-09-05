using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{
    public class PartnerConfig
    {
        public string Name { get; set; }

        public ICollection<KeyAndValue> ApiCredentials { get; set; }
        public ICollection<SubscriptionRow> Subscriptions { get; set; }


    }
}
