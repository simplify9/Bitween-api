using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using SW.Bitween.Domain;

namespace SW.Bitween.Resources.ApiGateways
{
    [HandlerName(nameof(AddPartner))]
    public class AddPartner : ICommandHandler<int, ApiGatewayPartnerCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public AddPartner(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int gatewayId, ApiGatewayPartnerCreate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

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

            // Check if partner already exists
            var existingPartner = gateway.Partners != null 
                ? gateway.Partners.FirstOrDefault(p => p.PartnerId == model.PartnerId && p.SubscriptionId == model.SubscriptionId)
                : null;

            if (existingPartner != null)
                throw new SWException("Partner already exists in this gateway");

            var partnerLink = new ApiGatewayPartner
            {
                ApiGatewayId = gatewayId,
                PartnerId = model.PartnerId,
                SubscriptionId = model.SubscriptionId
            };

            _dbContext.Add(partnerLink);
            await _dbContext.SaveChangesAsync();

            return null;
        }
    }
}

