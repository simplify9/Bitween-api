using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DelayedRetries
{
    public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
    {
        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View, Model.Permissions.Dashboard.View);

            var query = from delayedRetry in dbContext.Set<DelayedRetry>()
                        join xchange in dbContext.Set<Xchange>() on delayedRetry.Id equals xchange.Id
                        join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                        from result in xr.DefaultIfEmpty()
                        // Same left-join Xchanges/Search.cs uses — the UI lists a pending retry by
                        // what it carries, not by its id, so the properties have to come along.
                        join promoted in dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
                        from promoted in xp.DefaultIfEmpty()
                        join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                        join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
                        from subscriber in xs.DefaultIfEmpty()
                        select new DelayedRetryRow
                        {
                            Id = delayedRetry.Id,
                            On = delayedRetry.On,
                            SubscriptionId = xchange.SubscriptionId,
                            SubscriptionName = subscriber.Name,
                            DocumentId = xchange.DocumentId,
                            DocumentName = document.Name,
                            Exception = result.Exception,
                            StartedOn = xchange.StartedOn,
                            PromotedProperties = promoted == null ? null : promoted.Properties.InDefinedOrder(document.PromotedProperties),
                            RetryPolicyId = subscriber.RetryPolicyId,
                            RetryPolicyName = subscriber.RetryPolicy.Name
                        };

            query = query.OrderBy(r => r.On).AsNoTracking();

            if (lookup)
                return await query.Search(searchyRequest.Conditions)
                    .ToDictionaryAsync(k => k.Id, v => v.DocumentName);

            return new SearchyResponse<DelayedRetryRow>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts,
                    searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}
