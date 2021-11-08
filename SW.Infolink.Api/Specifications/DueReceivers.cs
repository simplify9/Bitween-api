using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace SW.Infolink
{
    class DueReceivers : ISpecification<Subscription>
    {
        public DueReceivers(DateTime? asOf = null)
        {
            if (asOf == null) asOf = DateTime.UtcNow;

            Criteria = e => e.ReceiveOn < asOf && e.Schedules.Any() && !e.Inactive && !e.IsRunning;
        }

        public Expression<Func<Subscription, bool>> Criteria { get; }
    }
}
