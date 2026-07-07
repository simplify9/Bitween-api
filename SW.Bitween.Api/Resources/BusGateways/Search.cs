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

            return new SearchyResponse<BusGatewayRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}
