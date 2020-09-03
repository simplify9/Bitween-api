//using SW.Infolink.Domain;
//using SW.Infolink.Model;
//using SW.PrimitiveTypes;
//using System;
//using System.Collections.Generic;
//using System.Linq.Expressions;
//using System.Text;

//namespace SW.Infolink
//{
//    class XchangesDueForDelivery : ISpecification<XchangeResult>
//    {
//        public XchangesDueForDelivery(DateTime? asOf = null)
//        {
//            if (asOf == null) asOf = DateTime.UtcNow;

//            Criteria = e => e.DeliverOn < asOf
//                && e.DeliveredOn == null
//                && e.Status == XchangeStatus.Success;
//        }

//        public Expression<Func<XchangeResult, bool>> Criteria { get; }

//    }
//}
