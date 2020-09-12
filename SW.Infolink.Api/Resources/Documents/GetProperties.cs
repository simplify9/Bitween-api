using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Documents
{
    [HandlerName("properties")]
    class GetProperties : IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public GetProperties(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, bool lookup = false)
        {
            var document = await dbContext.FindAsync<Document>(key);
            return document.PromotedProperties.ToDictionary(k => k.Key, v => v.Key);
        }
    }

}
