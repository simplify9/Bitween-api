using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace SW.Infolink
{
    class SubscribersByDocument : ISpecification<Subscription>
    {
        public SubscribersByDocument(int DocumentId, bool Inactive = false)
        {
            Criteria = e => e.DocumentId == DocumentId && e.Inactive == Inactive;
        }

        public Expression<Func<Subscription, bool>> Criteria { get; }
    }
}
