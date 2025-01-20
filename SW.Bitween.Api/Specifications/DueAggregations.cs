using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace SW.Bitween
{
    class DueAggregations : ISpecification<Subscription>
    {
        public DueAggregations(DateTime? asOf = null)
        {
            if (asOf == null) asOf = DateTime.UtcNow;

            Criteria = e => e.AggregateOn < asOf && e.Schedules.Any() && !e.Inactive;
        }

        public Expression<Func<Subscription, bool>> Criteria { get; }
    }
}
