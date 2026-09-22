using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
    public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
    {
        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.View);

            var query = from item in dbContext.Set<GlobalAdapterValuesSet>()
                        select new GlobalAdapterValuesSetRow
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Values = item.Values,
                            SecretProperties = item.SecretProperties
                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id, v => v.Name);
            }

            var result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync();

            // The keys stay — the reference picker offers {{globals.set.key}} from this list, and a
            // secret you cannot point at is no use to anyone. Only the values behind them go.
            foreach (var row in result)
                row.Values = AdapterSecretProperties.Mask(row.Values, row.SecretProperties);

            return new SearchyResponse<GlobalAdapterValuesSetRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = result
            };
        }
    }
}
