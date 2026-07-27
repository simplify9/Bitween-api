using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    [HandlerName(nameof(UpdateRoute))]
    public class UpdateRoute : ICommandHandler<int, BusGatewayRouteUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public UpdateRoute(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(int gatewayId, BusGatewayRouteUpdate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Edit);

            var gateway = await _dbContext.Set<BusGateway>()
                .FirstOrDefaultAsync(bg => bg.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with Id {gatewayId} not found");

            var route = await _dbContext.Set<BusGatewayRoute>()
                .FirstOrDefaultAsync(r => r.Id == model.RouteId && r.BusGatewayId == gatewayId);

            if (route == null)
                throw new SWNotFoundException($"Route with Id {model.RouteId} not found in gateway {gatewayId}");

            await AddRoute.ValidateSubscription(_dbContext, model.SubscriptionId, gateway.DocumentId);
            await AddRoute.ValidatePartner(_dbContext, model.PartnerId);

            route.SubscriptionId = model.SubscriptionId;
            route.PartnerId = model.PartnerId;
            route.MatchExpression = model.MatchExpression;

            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return null;
        }
    }
}
