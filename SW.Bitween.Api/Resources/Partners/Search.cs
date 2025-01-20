using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Partners
{
    class Search : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;

        public Search(BitweenDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from subscriber in dbContext.Set<Partner>()
                        select new PartnerRow
                        {
                            Id = subscriber.Id,
                            Name = subscriber.Name,
                            SubscriptionsCount = subscriber.Subscriptions.Count,
                            Keys = subscriber.ApiCredentials.Count,
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            return new SearchyResponse<PartnerRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };

        }
    }
}
