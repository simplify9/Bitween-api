using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using System.Collections.Generic;

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
                            // Every place the partner is wired in, matching the three groups the
                            // detail page lists. Counting only the direct subscription link left
                            // "Used by" reading "—" for partners plainly in use through a gateway.
                            SubscriptionsCount = subscriber.Subscriptions.Count
                                + dbContext.Set<ApiGatewayPartner>().Count(g => g.PartnerId == subscriber.Id)
                                + dbContext.Set<BusGatewayRoute>().Count(r => r.PartnerId == subscriber.Id),
                            Keys = subscriber.ApiCredentials.Count,
                            // Selected so the key names can be lifted out below. AdapterProperties
                            // is a JSON column, so picking its keys in SQL would be provider-specific.
                            AdapterProperties = subscriber.AdapterProperties,
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            var result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync();

            foreach (var row in result)
            {
                row.PropertyKeys = row.AdapterProperties?.Keys.ToList() ?? new List<string>();
                // Values can be secrets, so the list ships the names and nothing else.
                row.AdapterProperties = null;
            }

            return new SearchyResponse<PartnerRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = result
            };

        }
    }
}
