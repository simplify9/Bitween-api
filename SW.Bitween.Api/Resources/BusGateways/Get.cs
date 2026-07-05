using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
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
            var gateway = await _dbContext.Set<BusGateway>()
                .AsNoTracking()
                .Include(bg => bg.Routes)
                    .ThenInclude(r => r.Subscription)
                .Include(bg => bg.Routes)
                    .ThenInclude(r => r.Partner)
                .FirstOrDefaultAsync(bg => bg.Id == key);

            if (gateway == null)
                throw new SWNotFoundException($"BusGateway with id '{key}' was not found");

            var documentName = await _dbContext.Set<Document>()
                .Where(d => d.Id == gateway.DocumentId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync();

            return new BusGatewayRow
            {
                Id = gateway.Id,
                Name = gateway.Name,
                DocumentId = gateway.DocumentId,
                DocumentName = documentName,
                RoutesCount = gateway.Routes.Count,
                Routes = gateway.Routes.Select(r => new BusGatewayRouteDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,
                    SubscriptionName = r.Subscription != null ? r.Subscription.Name : null,
                    PartnerId = r.PartnerId,
                    PartnerName = r.Partner != null ? r.Partner.Name : null,
                    MatchExpression = r.MatchExpression
                }).ToList()
            };
        }
    }
}
