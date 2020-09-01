using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    class XchangeSubmittedEvent : BaseDomainEvent
    {
        public Xchange Xchange { get; set; }
        public Subscriber Subscriber { get; set; }
    }
}
