using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.ApiGateways
{
    public class Delete : IDeleteHandler<int, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.Delete);

            var gateway = await _dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .FirstOrDefaultAsync(ag => ag.Id == key);

            if (gateway == null)
                throw new SWNotFoundException($"ApiGateway with Id {key} not found");

            // Partners are FK-restricted to the gateway; remove them explicitly before the gateway.
            if (gateway.Partners != null && gateway.Partners.Count > 0)
                _dbContext.RemoveRange(gateway.Partners);

            _dbContext.Remove(gateway);
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}

