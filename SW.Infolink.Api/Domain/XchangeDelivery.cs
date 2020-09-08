using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    class XchangeDelivery : BaseEntity<string>
    {

        private XchangeDelivery()
        {
        }

        public XchangeDelivery(string xchangeId)
        {
            Id = xchangeId; // Guid.NewGuid().ToString("N");
            //XchangeId = xchangeId;
            DeliveredOn = DateTime.UtcNow;
        }

        //public string XchangeId { get; private set; }
        public DateTime DeliveredOn { get; private set; }

    }
}
