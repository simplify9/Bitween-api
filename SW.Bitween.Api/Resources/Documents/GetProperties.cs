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
    public class GetProperties(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;

        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

            var document = await dbContext.FindAsync<Document>(key) ?? throw new SWNotFoundException(key.ToString());
            return document.PromotedProperties.ToDictionary(k => k.Key, v => v.Key);
        }
    }

}
