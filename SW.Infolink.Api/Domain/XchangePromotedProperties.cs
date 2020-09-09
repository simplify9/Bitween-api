using SW.PrimitiveTypes;
using System;
using System.Collections;
using System.Collections.Generic;

namespace SW.Infolink.Domain
{
    class XchangePromotedProperties : BaseEntity<string>
    {
        private XchangePromotedProperties()
        {
        }

        public XchangePromotedProperties(string xchangeId, IDictionary<string, string> properties)
        {
            Id = xchangeId;
            Properties = properties.ToDictionary(); 
        }

        public IReadOnlyDictionary<string, string> Properties { get; private set; }
    }
}
