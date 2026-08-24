using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.ApiGateways
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await _requestContext.EnsurePermission(_dbContext, Model.Permissions.ApiGateways.View);

            var query = from gateway in _dbContext.Set<ApiGateway>()
                        select new ApiGatewayRow
                        {
                            Id = gateway.Id,
                            Name = gateway.Name,
                            UrlName = gateway.UrlName,
                            Inactive = gateway.Inactive,
                            PartnersCount = gateway.Partners.Count
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            // Apply ordering by Id descending
            query = query.OrderByDescending(g => g.Id);

            var totalCount = await query.Search(searchyRequest.Conditions).CountAsync();
            var result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync();

            // The list screen needs full attachment detail up front (not just a
            // count), so hydrate it with one grouped query instead of Get.cs's
            // per-row Include (gateways are few, so this stays a single round trip).
            var ids = result.Select(r => r.Id).ToList();
            var partnersByGateway = (await _dbContext.Set<ApiGatewayPartner>()
                .AsNoTracking()
                .Where(p => ids.Contains(p.ApiGatewayId))
                .Include(p => p.Partner)
                .Include(p => p.Subscription)
                .ToListAsync())
                .ToLookup(p => p.ApiGatewayId);
            foreach (var row in result)
            {
                row.Partners = partnersByGateway[row.Id].Select(p => new ApiGatewayPartnerDto
                {
                    PartnerId = p.PartnerId,
                    SubscriptionId = p.SubscriptionId,
                    PartnerName = p.Partner.Name,
                    SubscriptionName = p.Subscription.Name
                }).ToList();
            }

            return new SearchyResponse<ApiGatewayRow>
            {
                TotalCount = totalCount,
                Result = result
            };
        }
    }
}

