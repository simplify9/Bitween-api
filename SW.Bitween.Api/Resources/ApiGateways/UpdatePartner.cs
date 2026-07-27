using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using SW.Bitween.Domain;

namespace SW.Bitween.Resources.ApiGateways
{
    [HandlerName(nameof(UpdatePartner))]
    public class UpdatePartner : ICommandHandler<int, ApiGatewayPartnerCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public UpdatePartner(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int gatewayId, ApiGatewayPartnerCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.Edit);

            var gateway = await _dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .FirstOrDefaultAsync(ag => ag.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"ApiGateway with Id {gatewayId} not found");

            // Validate subscription exists and is of type GatewayApiCall
            var subscription = await _dbContext.Set<Subscription>()
                .FirstOrDefaultAsync(s => s.Id == model.SubscriptionId);

            if (subscription == null)
                throw new SWNotFoundException($"Subscription with Id {model.SubscriptionId} not found");

            if (subscription.Type != SubscriptionType.GatewayApiCall)
                throw new SWException($"Subscription must be of type GatewayApiCall. Current type: {subscription.Type}");

            var partnerLink = gateway.Partners?
                .FirstOrDefault(p => p.PartnerId == model.PartnerId);

            if (partnerLink == null)
                throw new SWNotFoundException($"Partner with Id {model.PartnerId} not found in gateway {gatewayId}");

            partnerLink.SubscriptionId = model.SubscriptionId;

            await _dbContext.SaveChangesAsync();

            return null;
        }
    }
}

