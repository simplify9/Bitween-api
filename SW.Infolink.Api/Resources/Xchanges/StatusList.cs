using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Xchanges
{
    [HandlerName("statuslist")]
    class StatusList : ISearchyHandler
    {
        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            //throw new NotImplementedException();
            return new Dictionary<string, string>
            {
                {"0", "Running" },
                {"1", "Success" },
                {"2", "Success with bad response" },
                {"3", "Failed" },
                
            };
        }
    }
}
