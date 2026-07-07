using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Bitween.Model
{
    public class FilterResult
    {
        public FilterResult()
        {
            Hits = new HashSet<int>();
            GatewayHits = new List<GatewayHit>();
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<int> Hits { get; set; }

        // Bus-gateway route matches: run the assigned subscription, optionally with a partner's values.
        public List<GatewayHit> GatewayHits { get; set; }
        public IDictionary<string, string> Properties { get; set; }
    }

    public class GatewayHit
    {
        public int SubscriptionId { get; set; }
        public int? PartnerId { get; set; }
    }
}
