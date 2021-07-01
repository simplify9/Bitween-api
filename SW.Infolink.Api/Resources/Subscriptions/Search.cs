using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.Subscriptions
{
    class Search : ISearchyHandler
    {
        private readonly InfolinkDbContext dbContext;

        public Search(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {

            var query = from subscriber in dbContext.Set<Subscription>()
                        join document in dbContext.Set<Document>() on subscriber.DocumentId equals document.Id
                        select new SubscriptionSearch
                        {
                            Id = subscriber.Id,
                            Name = subscriber.Name,
                            Type = subscriber.Type,
                            DocumentId = subscriber.DocumentId,
                            DocumentName = document.Name,
                            HandlerId = subscriber.HandlerId,
                            Inactive = subscriber.Inactive,
                            MapperId = subscriber.MapperId,
                            AggregationForId = subscriber.AggregationForId,
                            Temporary = subscriber.Temporary,
                            ReceiveOn = subscriber.ReceiveOn,
                            PausedOn = subscriber.PausedOn,

                        };

            query = query.AsNoTracking();

            if (lookup)
            {
                return await query.Search(searchyRequest.Conditions).ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }

            return new SearchyResponse<SubscriptionSearch>
            {
                TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
            };
        }
    }
}
