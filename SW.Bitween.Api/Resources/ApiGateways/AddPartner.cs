using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Resources.Subscriptions;

namespace SW.Bitween.Resources.ApiGateways
{
    [HandlerName(nameof(AddPartner))]
    public class AddPartner : ICommandHandler<int, ApiGatewayPartnerCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly AdapterRequirements _adapterRequirements;
        private readonly IInfolinkCache _cache;

        public AddPartner(BitweenDbContext dbContext, RequestContext requestContext,
            AdapterRequirements adapterRequirements, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _adapterRequirements = adapterRequirements;
            _cache = cache;
        }

        public async Task<object> Handle(int gatewayId, ApiGatewayPartnerCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.Edit);

            var gateway = await _dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .FirstOrDefaultAsync(ag => ag.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"ApiGateway with Id {gatewayId} not found");

            InlineIntegration.EnsureExactlyOne(model.SubscriptionId, model.NewIntegration);

            var partnerLink = new ApiGatewayPartner
            {
                ApiGatewayId = gatewayId,
                PartnerId = model.PartnerId
            };

            if (model.NewIntegration != null)
            {
                // Staged, not saved — the attachment takes its foreign key from the subscription EF
                // is tracking, so the pair lands on the one SaveChangesAsync below or not at all.
                // Nothing to check for a duplicate against: an integration that does not exist yet
                // cannot already be attached.
                // An API gateway is not bound to an information type the way a bus gateway is,
                // so this one comes from the caller.
                var integration = await InlineIntegration.Stage(
                    _dbContext, _adapterRequirements, model.NewIntegration,
                    model.NewIntegration.DocumentId, SubscriptionType.GatewayApiCall);
                partnerLink.Subscription = integration;
            }
            else
            {
                // Validate subscription exists and is of type GatewayApiCall
                var subscription = await _dbContext.Set<Subscription>()
                    .FirstOrDefaultAsync(s => s.Id == model.SubscriptionId.Value);

                if (subscription == null)
                    throw new SWNotFoundException($"Subscription with Id {model.SubscriptionId} not found");

                if (subscription.Type != SubscriptionType.GatewayApiCall)
                    throw new SWException($"Subscription must be of type GatewayApiCall. Current type: {subscription.Type}");

                // Check if partner already exists
                var existingPartner = gateway.Partners != null
                    ? gateway.Partners.FirstOrDefault(p => p.PartnerId == model.PartnerId && p.SubscriptionId == model.SubscriptionId)
                    : null;

                if (existingPartner != null)
                    throw new SWException("Partner already exists in this gateway");

                partnerLink.SubscriptionId = model.SubscriptionId.Value;
            }

            _dbContext.Add(partnerLink);
            await _dbContext.SaveChangesAsync();
            // Attaching an existing integration changes nothing the cache holds, but staging a new
            // one above creates a Subscription — and unconditional is what AddRoute does.
            await _cache.BroadcastRevoke();

            return null;
        }
    }
}

