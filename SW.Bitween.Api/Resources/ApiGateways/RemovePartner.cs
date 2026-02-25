using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace SW.Bitween.Resources.ApiGateways
{
    [HandlerName(nameof(RemovePartner))]
    public class RemovePartner : ICommandHandler<int, RemovePartnerRequest, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public RemovePartner(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int gatewayId, RemovePartnerRequest request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var gateway = await _dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .FirstOrDefaultAsync(ag => ag.Id == gatewayId);

            if (gateway == null)
                throw new SWNotFoundException($"ApiGateway with Id {gatewayId} not found");

            var partnerLink = gateway.Partners?
                .FirstOrDefault(p => p.PartnerId == request.PartnerId);

            if (partnerLink == null)
                throw new SWNotFoundException($"Partner with Id {request.PartnerId} not found in gateway {gatewayId}");

            _dbContext.Remove(partnerLink);
            await _dbContext.SaveChangesAsync();

            return null;
        }
    }

    public class RemovePartnerRequest
    {
        public int PartnerId { get; set; }
    }
}

