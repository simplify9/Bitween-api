using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Create : ICommandHandler<BusGatewayCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(BusGatewayCreate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var documentExists = await _dbContext.Set<Document>().AnyAsync(d => d.Id == model.DocumentId);
            if (!documentExists)
                throw new SWNotFoundException($"Document with Id {model.DocumentId} not found");

            var entity = new BusGateway
            {
                Name = model.Name,
                DocumentId = model.DocumentId
            };

            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return entity.Id;
        }
    }
}
