using System.Linq;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Api.Resources.Documents
{
    class Update : ICommandHandler<int, DocumentUpdate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly IInfolinkCache _infolinkCache;
        private readonly RequestContext _requestContext;
        private readonly IBroadcast _broadcast;


        public Update(InfolinkDbContext dbContext, IInfolinkCache infolinkCache, RequestContext requestContext,
            IBroadcast broadcast)
        {
            this._dbContext = dbContext;
            _infolinkCache = infolinkCache;
            _requestContext = requestContext;
            _broadcast = broadcast;
        }

        public async Task<object> Handle(int key, DocumentUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Document>(key);

            var busTypeNameDuplicated = await _dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .Where(i => i.BusMessageTypeName == entity.BusMessageTypeName)
                .AnyAsync();

            if (busTypeNameDuplicated)
                throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME",
                    "Cant use duplicated bus Message type name");


            var trail = new DocumentTrail(DocumentTrailCode.Updated, entity);
            entity.SetDictionaries(model.PromotedProperties.ToDictionary());
            _dbContext.Entry(entity).SetProperties(model);

            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            _infolinkCache.BroadcastRevoke();
            await _broadcast.RefreshConsumers();
            return null;
        }
    }
}