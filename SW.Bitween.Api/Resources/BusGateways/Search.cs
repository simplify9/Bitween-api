using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.BusGateways
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext _dbContext;

        public Search(BitweenDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var documents = _dbContext.Set<Document>();

            var query = from gateway in _dbContext.Set<BusGateway>()
                        select new BusGatewayRow
                        {
                            Id = gateway.Id,
                            Name = gateway.Name,
                            DocumentId = gateway.DocumentId,
                            DocumentName = documents.Where(d => d.Id == gateway.DocumentId)
                                .Select(d => d.Name).FirstOrDefault(),
                            RoutesCount = gateway.Routes.Count
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            query = query.OrderByDescending(g => g.Id);

            var totalCount = await query.Search(searchyRequest.Conditions).CountAsync();
            var result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync();

            // The list screen needs full route detail up front (not just a count),
            // so hydrate it with one grouped query instead of Get.cs's per-row
            // Include (gateways are few, so this stays a single round trip).
            var ids = result.Select(r => r.Id).ToList();
            var routesByGateway = (await _dbContext.Set<BusGatewayRoute>()
                .AsNoTracking()
                .Where(r => ids.Contains(r.BusGatewayId))
                .Include(r => r.Subscription)
                .Include(r => r.Partner)
                .ToListAsync())
                .ToLookup(r => r.BusGatewayId);
            foreach (var row in result)
            {
                row.Routes = routesByGateway[row.Id].Select(r => new BusGatewayRouteDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,
                    SubscriptionName = r.Subscription != null ? r.Subscription.Name : null,
                    PartnerId = r.PartnerId,
                    PartnerName = r.Partner != null ? r.Partner.Name : null,
                    MatchExpression = r.MatchExpression
                }).ToList();
            }

            return new SearchyResponse<BusGatewayRow>
            {
                TotalCount = totalCount,
                Result = result
            };
        }
    }
}
