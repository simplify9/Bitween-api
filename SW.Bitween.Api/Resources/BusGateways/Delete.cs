using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Delete : IDeleteHandler<int, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(int key)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var gateway = await _dbContext.Set<BusGateway>()
                .Include(bg => bg.Routes)
                .FirstOrDefaultAsync(bg => bg.Id == key);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with Id {key} not found");

            // Routes are FK-restricted to the gateway; remove them explicitly before the gateway.
            if (gateway.Routes != null && gateway.Routes.Count > 0)
                _dbContext.RemoveRange(gateway.Routes);

            _dbContext.Remove(gateway);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return null;
        }
    }
}
