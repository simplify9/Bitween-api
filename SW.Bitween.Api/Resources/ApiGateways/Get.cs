using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.ApiGateways
{
    public class Get : IGetHandler<int, object>
    {
        private readonly BitweenDbContext _dbContext;

        public Get(BitweenDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> Handle(int key)
        {
            return await _dbContext.Set<ApiGateway>()
                .AsNoTracking()
                .Include(ag => ag.Partners)
                    .ThenInclude(p => p.Partner)
                .Include(ag => ag.Partners)
                    .ThenInclude(p => p.Subscription)
                .Where(ag => ag.Id == key)
                .Select(gateway => new ApiGatewayUpdate
                {
                    Name = gateway.Name,
                    UrlName = gateway.UrlName,
                    Partners = gateway.Partners.Select(p => new ApiGatewayPartnerDto
                    {
                        PartnerId = p.PartnerId,
                        SubscriptionId = p.SubscriptionId,
                        PartnerName = p.Partner.Name,
                        SubscriptionName = p.Subscription.Name
                    }).ToList()
                })
                .SingleOrDefaultAsync();
        }
    }
}

