using System.Linq;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Documents
{
    public class Update : ICommandHandler<int, DocumentUpdate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly RequestContext _requestContext;
        private readonly IBroadcast _broadcast;


        public Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext,
            IBroadcast broadcast)
        {
            this._dbContext = dbContext;
            _BitweenCache = BitweenCache;
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
                .Where(i => !string.IsNullOrEmpty(i.BusMessageTypeName))
                .Where(i => i.BusMessageTypeName == model.BusMessageTypeName)
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
            _BitweenCache.BroadcastRevoke();
            await _broadcast.RefreshConsumers();
            return null;
        }
    }
}