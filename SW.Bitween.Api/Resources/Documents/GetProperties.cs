using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Documents
{
    [HandlerName("properties")]
    public class GetProperties : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public GetProperties(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

            var document = await dbContext.FindAsync<Document>(key);
            return document.PromotedProperties.ToDictionary(k => k.Key, v => v.Key);
        }
    }

}
