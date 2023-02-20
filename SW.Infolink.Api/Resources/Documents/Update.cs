using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Api.Resources.Documents
{
    class Update : ICommandHandler<int, DocumentUpdate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly IInfolinkCache _infolinkCache;
        private readonly RequestContext _requestContext;

        public Update(InfolinkDbContext dbContext, IInfolinkCache infolinkCache, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _infolinkCache = infolinkCache;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, DocumentUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            var entity = await _dbContext.FindAsync<Document>(key);
            var trail = new DocumentTrail(DocumentTrailCode.Updated, entity);


            entity.SetDictionaries(model.PromotedProperties.ToDictionary());
            _dbContext.Entry(entity).SetProperties(model);

            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            _infolinkCache.Revoke();
            return null;
        }
    }
}