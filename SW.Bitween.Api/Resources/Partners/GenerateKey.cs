using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Partners
{
    [HandlerName("generatekey") ]
    public class GenerateKey : IQueryHandler<object>
    {
        public Task<object> Handle()
        {
            return Task.FromResult((object)Guid.NewGuid().ToString("N"));
        }
    }
}
