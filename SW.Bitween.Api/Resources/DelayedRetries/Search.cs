using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DelayedRetries
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
            var query = from delayedRetry in _dbContext.Set<DelayedRetry>()
                        join xchange in _dbContext.Set<Xchange>() on delayedRetry.Id equals xchange.Id
                        join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                        from result in xr.DefaultIfEmpty()
                        join document in _dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                        join subscriber in _dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
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
                            StartedOn = xchange.StartedOn
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
