using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Partners
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.View);

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
