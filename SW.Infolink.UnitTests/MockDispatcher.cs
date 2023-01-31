using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.UnitTests
{ 
    public class MockDispatcher : IDomainEventDispatcher
    {
        public Task Dispatch(IDomainEvent domainEvent)
        {
            //throw new NotImplementedException();
            return Task.CompletedTask;
        }
    }
}
