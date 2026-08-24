using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.Bitween.Resources.Subscriptions;
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

        private readonly AdapterRequirements _adapterRequirements;

        public AddRoute(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache,
            AdapterRequirements adapterRequirements)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
            _adapterRequirements = adapterRequirements;
        }

        public async Task<object> Handle(int gatewayId, BusGatewayRouteCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.BusGateways.Edit);

            var gateway = await _dbContext.Set<BusGateway>()
                .FirstOrDefaultAsync(bg => bg.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with Id {gatewayId} not found");

            InlineIntegration.EnsureExactlyOne(model.SubscriptionId, model.NewIntegration);
            await ValidatePartner(_dbContext, model.PartnerId);

            var route = new BusGatewayRoute
            {
                BusGatewayId = gatewayId,
                PartnerId = model.PartnerId,
                MatchExpression = model.MatchExpression
            };

            if (model.NewIntegration != null)
            {
                // Staged, not saved: EF fills the route's foreign key from the subscription it is
                // tracking, so both rows go in on the one SaveChangesAsync below. A route pointing
                // at an integration that was never committed is not a state that can happen.
                var integration = await InlineIntegration.Stage(
                    _dbContext, _adapterRequirements, model.NewIntegration, gateway.DocumentId,
                    SubscriptionType.BusGateway);
                route.Subscription = integration;
            }
            else
            {
                await ValidateSubscription(_dbContext, model.SubscriptionId.Value, gateway.DocumentId);
                route.SubscriptionId = model.SubscriptionId.Value;
            }

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
