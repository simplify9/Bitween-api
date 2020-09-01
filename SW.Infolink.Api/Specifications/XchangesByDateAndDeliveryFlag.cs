using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace SW.Infolink
{
  public class XchangesByDateAndDeliveryFlag : ISpecification<Xchange>
    {
        public XchangesByDateAndDeliveryFlag(int suscriberId, DateTime from, DateTime? to, bool excludeDelivered = true)
        {
            Criteria = e => e.FinishedOn > from
                && (e.Status == XchangeStatus.Success)
                && (e.SubscriberId == suscriberId)
                && (to == null || e.FinishedOn <= to)
                && (excludeDelivered || e.DeliveredOn != null);
        }

        public Expression<Func<Xchange, bool>> Criteria { get; }

    }
}
