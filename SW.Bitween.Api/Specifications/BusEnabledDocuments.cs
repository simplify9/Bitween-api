using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace SW.Bitween
{
    class BusEnabledDocuments : ISpecification<Document>
    {
        public BusEnabledDocuments()
        {
            Criteria = e => e.BusEnabled && !string.IsNullOrWhiteSpace(e.BusMessageTypeName) ;
        }

        public Expression<Func<Document, bool>> Criteria { get; }
    }
}
