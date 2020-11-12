using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    public class XchangeAggregation : BaseEntity<string>
    {
        private XchangeAggregation()
        {
        }

        public XchangeAggregation(string xchangeId, string aggregationXchangeId)
        {
            Id = xchangeId; 
            AggregatedOn = DateTime.UtcNow;
            AggregationXchangeId = aggregationXchangeId;
        }

        public DateTime AggregatedOn { get; private set; }
        public string AggregationXchangeId { get; private set; }

    }
}
