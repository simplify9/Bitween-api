using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    [HandlerName(nameof(AddRoute))]
    public class AddRoute : ICommandHandler<int, BusGatewayRouteCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public AddRoute(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(int gatewayId, BusGatewayRouteCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Edit);

            var gateway = await _dbContext.Set<BusGateway>()
                .FirstOrDefaultAsync(bg => bg.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with Id {gatewayId} not found");

            await ValidateSubscription(_dbContext, model.SubscriptionId, gateway.DocumentId);
            await ValidatePartner(_dbContext, model.PartnerId);

            var route = new BusGatewayRoute
            {
                BusGatewayId = gatewayId,
                SubscriptionId = model.SubscriptionId,
                PartnerId = model.PartnerId,
                MatchExpression = model.MatchExpression
            };

            _dbContext.Add(route);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
            return route.Id;
        }

        internal static async Task ValidateSubscription(BitweenDbContext dbContext, int subscriptionId,
            int gatewayDocumentId)
        {
            var subscription = await dbContext.Set<Subscription>()
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (subscription == null)
                throw new SWNotFoundException($"Subscription with Id {subscriptionId} not found");

            if (subscription.Type != SubscriptionType.BusGateway)
                throw new SWException($"Subscription must be of type BusGateway. Current type: {subscription.Type}");

            if (subscription.DocumentId != gatewayDocumentId)
                throw new SWException("Subscription must be bound to the same document as the bus gateway");
        }

        internal static async Task ValidatePartner(BitweenDbContext dbContext, int? partnerId)
        {
            if (!partnerId.HasValue)
                return;

            var partnerExists = await dbContext.Set<Partner>().AnyAsync(p => p.Id == partnerId.Value);
            if (!partnerExists)
                throw new SWNotFoundException($"Partner with Id {partnerId.Value} not found");
        }
    }
}
