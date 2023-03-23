using System.Collections.Generic;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using Document = SW.Infolink.Domain.Document;

namespace SW.Infolink.Resources.Subscriptions
{
    class Search : ISearchyHandler
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly List<string> _edgeCaseProperties;

        public Search(InfolinkDbContext dbContext)
        {
            this._dbContext = dbContext;

            _edgeCaseProperties = new List<string>
            {
                "rawsubscriptionproperties"
            };
        }


        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from subscriber in _dbContext.Set<Subscription>()
                join document in _dbContext.Set<Document>() on subscriber.DocumentId equals document.Id
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
                    IsRunning = subscriber.IsRunning,
                    MapperProperties = subscriber.MapperProperties.ToKeyAndValueCollection(),
                    HandlerProperties = subscriber.HandlerProperties.ToKeyAndValueCollection(),
                    ReceiverProperties = subscriber.ReceiverProperties.ToKeyAndValueCollection(),
                    ValidatorProperties = subscriber.ValidatorProperties.ToKeyAndValueCollection(),
                };

            query = query.AsNoTracking().AsQueryable();
            var edgeCaseFilters = HandleSearchyEdgeCases(searchyRequest.Conditions);

            if (lookup)
            {
                return await query.OrderBy(s => s.Name).Search(searchyRequest.Conditions)
                    .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }


            var count = await query.Search(searchyRequest.Conditions).CountAsync();

            if (edgeCaseFilters.Any())
            {
                return await SearchWithEdgeCases(query, edgeCaseFilters, searchyRequest, count);
            }

            return new SearchyResponse<SubscriptionSearch>
            {
                TotalCount = count,
                Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize,
                    searchyRequest.PageIndex).ToListAsync()
            };
        }

        private async Task<SearchyResponse<SubscriptionSearch>> SearchWithEdgeCases(
            IQueryable<SubscriptionSearch> query, IEnumerable<SearchyFilter> edgeCaseFilters,
            SearchyRequest searchyRequest,
            int count)
        {
            var data = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts).ToListAsync();

            foreach (var edgeCaseFilter in edgeCaseFilters)
            {
                switch (edgeCaseFilter.Field.ToLower())
                {
                    case "rawsubscriptionproperties":
                    {
                        var searchTerm = edgeCaseFilter.ValueString.ToLower();

                        data = data.Where(i =>
                                i.HandlerProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                                i.ReceiverProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                                i.MapperProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                                i.ValidatorProperties.Any(p => p.Value.ToLower().Contains(searchTerm))
                            )
                            .Skip(searchyRequest.PageSize * searchyRequest.PageIndex).Take(searchyRequest.PageSize)
                            .ToList();

                        break;
                    }
                }
            }

            return new SearchyResponse<SubscriptionSearch>
            {
                TotalCount = count,
                Result = data
            };
        }


        private ICollection<SearchyFilter> HandleSearchyEdgeCases(ICollection<SearchyCondition> filters)
        {
            var list = new List<SearchyFilter>();
            var edgeCaseFilters = filters?.SelectMany(i => i.Filters)
                .Where(i => _edgeCaseProperties.Contains(i.Field.ToLower())).ToList();

            list.AddRange(edgeCaseFilters);
            foreach (var con in filters)
            {
                foreach (var edgeCase in edgeCaseFilters)
                {
                    con.Filters.Remove(edgeCase);
                }
            }

            return list;
        }
    }
}