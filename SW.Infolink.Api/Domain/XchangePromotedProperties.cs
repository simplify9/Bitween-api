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
            Id = xchangeId; // Guid.NewGuid().ToString("N");
            //XchangeId = xchangeId;
            Properties = properties.ToDictionary(); 
        }

        //public string XchangeId { get; private set; }
        public IReadOnlyDictionary<string, string> Properties { get; private set; }
    }
}
