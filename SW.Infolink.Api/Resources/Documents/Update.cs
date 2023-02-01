using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
{
    class Update : ICommandHandler<int, DocumentUpdate>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly IInfolinkCache _infolinkCache;

        public Update(InfolinkDbContext dbContext, IInfolinkCache infolinkCache)
        {
            this.dbContext = dbContext;
            _infolinkCache = infolinkCache;
        }

        async public Task<object> Handle(int key, DocumentUpdate model)
        {
            var entity = await dbContext.FindAsync<Document>(key);
            entity.SetDictionaries(model.PromotedProperties.ToDictionary());
            dbContext.Entry(entity).SetProperties(model);
            await dbContext.SaveChangesAsync();
            _infolinkCache.Revoke();
            return null;
        }
    }
}
