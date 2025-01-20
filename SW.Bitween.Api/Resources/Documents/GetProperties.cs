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
    class GetProperties : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;

        public GetProperties(BitweenDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key)
        {
            var document = await dbContext.FindAsync<Document>(key);
            return document.PromotedProperties.ToDictionary(k => k.Key, v => v.Key);
        }
    }

}
