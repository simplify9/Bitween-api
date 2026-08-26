using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Xchanges
{
    [HandlerName("statuslist")]
    public class StatusList : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public StatusList(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

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
