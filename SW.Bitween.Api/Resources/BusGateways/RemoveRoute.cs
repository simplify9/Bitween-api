using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    [HandlerName(nameof(RemoveRoute))]
    public class RemoveRoute : ICommandHandler<int, RemoveRouteRequest, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public RemoveRoute(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(int gatewayId, RemoveRouteRequest request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var route = await _dbContext.Set<BusGatewayRoute>()
                .FirstOrDefaultAsync(r => r.Id == request.RouteId && r.BusGatewayId == gatewayId);

            if (route == null)
                throw new SWNotFoundException($"Route with Id {request.RouteId} not found in gateway {gatewayId}");

            _dbContext.Remove(route);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return null;
        }
    }
}
