using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;

namespace SW.Infolink.Domain
{
    public class XchangePromotedProperties : BaseEntity<string>
    {
        private XchangePromotedProperties()
        {
        }

        public XchangePromotedProperties(string xchangeId, FilterResult filterResult)
        {
            Id = xchangeId;
            Properties = filterResult.Properties.ToDictionary();
            Hits = filterResult.Hits?.ToArray() ?? new int[] { };
            
            
            foreach (var (key, value) in filterResult.Properties.ToDictionary())
            {
                var val = $"{key}:{value}";
                if (string.IsNullOrEmpty(PropertiesRaw)) PropertiesRaw = val;
                else PropertiesRaw += $",{val}";
            }
        }

        public IReadOnlyDictionary<string, string> Properties { get; private set; }
        public string PropertiesRaw { get; private set; }
        public int[] Hits { get; private set; }

    }
}
