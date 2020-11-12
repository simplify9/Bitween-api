using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    public class XchangeDelivery : BaseEntity<string>
    {
        private XchangeDelivery()
        {
        }

        public XchangeDelivery(string xchangeId)
        {
            Id = xchangeId; 
            DeliveredOn = DateTime.UtcNow;
        }

        public DateTime DeliveredOn { get; private set; }
    }
}
