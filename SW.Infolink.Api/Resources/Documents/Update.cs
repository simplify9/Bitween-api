using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
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

        public async Task<object> Handle(int key, DocumentUpdate model)
        {
            var entity = await dbContext.FindAsync<Document>(key);
            var trail = new DocumentTrail(DocumentTrailCode.Updated, entity);


            entity.SetDictionaries(model.PromotedProperties.ToDictionary());
            dbContext.Entry(entity).SetProperties(model);

             trail.SetAfter(entity);
             dbContext.Add(trail);
            await dbContext.SaveChangesAsync();
            _infolinkCache.Revoke();
            return null;
        }
    }
}