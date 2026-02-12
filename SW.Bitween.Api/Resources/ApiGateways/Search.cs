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

        public Search(BitweenDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from gateway in _dbContext.Set<ApiGateway>()
                        select new ApiGatewayRow
                        {
                            Id = gateway.Id,
                            Name = gateway.Name,
                            UrlName = gateway.UrlName,
                            PartnersCount = gateway.Partners.Count
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            // Apply ordering by Id descending
            query = query.OrderByDescending(g => g.Id);

            return new SearchyResponse<ApiGatewayRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}

