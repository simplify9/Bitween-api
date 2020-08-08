using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink
{
    public class SubscriptionCredential
    {
        public SubscriptionCredential(int subscriberId, string key)
        {
            SubscriberId = subscriberId;
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public int SubscriberId { get; set; }

        public string Key { get; set; }
    }
}
