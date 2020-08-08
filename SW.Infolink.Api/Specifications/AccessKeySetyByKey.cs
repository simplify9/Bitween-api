using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace SW.Infolink
{
   public class AccessKeySetyByKey : ISpecification<AccessKeySet>
    {
        public AccessKeySetyByKey(int id, string key)
        {
            Criteria = e => e.Id == id //credential.SubscriberId 
                && (e.Key1 == key || e.Key2 == key);
        }

        public Expression<Func<AccessKeySet, bool>> Criteria { get; }
    }
}
