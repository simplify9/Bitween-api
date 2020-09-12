using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;

namespace SW.Infolink.Domain
{
    class XchangePromotedProperties : BaseEntity<string>
    {
        private XchangePromotedProperties()
        {
        }

        public XchangePromotedProperties(string xchangeId, FilterResult filterResult)
        {
            Id = xchangeId;
            Properties = filterResult.Properties.ToDictionary();
            Hits = filterResult.Hits?.ToArray() ?? new int[] { };
        }

        public IReadOnlyDictionary<string, string> Properties { get; private set; }
        public int[] Hits { get; private set; }

    }
}
