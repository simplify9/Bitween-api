using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Partners
{
    [HandlerName("generatekey") ]
    class GenerateKey : IQueryHandler
    {
        public Task<object> Handle()
        {
            return Task.FromResult((object)Guid.NewGuid().ToString("N"));
        }
    }
}
