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
            var gateway = await _dbContext.Set<ApiGateway>()
                .AsNoTracking()
                .Include(ag => ag.Partners)
                    .ThenInclude(p => p.Partner)
                .Include(ag => ag.Partners)
                    .ThenInclude(p => p.Subscription)
                .FirstOrDefaultAsync(ag => ag.Id == key);

            if (gateway == null)
                throw new SWNotFoundException($"ApiGateway with id '{key}' was not found");

            return new ApiGatewayRow
            {
                Id = gateway.Id,
                Name = gateway.Name,
                UrlName = gateway.UrlName,
                PartnersCount = gateway.Partners.Count,
                Partners = gateway.Partners.Select(p => new ApiGatewayPartnerDto
                {
                    PartnerId = p.PartnerId,
                    SubscriptionId = p.SubscriptionId,
                    PartnerName = p.Partner.Name,
                    SubscriptionName = p.Subscription.Name
                }).ToList()
            };
        }
    }
}

