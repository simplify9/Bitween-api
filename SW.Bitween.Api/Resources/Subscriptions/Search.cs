using System.Collections.Generic;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using Document = SW.Bitween.Domain.Document;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly List<string> _edgeCaseProperties;

        public Search(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;

            _edgeCaseProperties = new List<string>
            {
                "rawsubscriptionproperties",
                "rawfiltersproperties",
                "name"
            };
        }


        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

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
                    DataSourceId = subscriber.DataSourceId,
                    Inactive = subscriber.Inactive,
                    MapperId = subscriber.MapperId,
                    ValidatorId = subscriber.ValidatorId,
                    ReceiverId = subscriber.ReceiverId,
                    AggregationForId = subscriber.AggregationForId,
                    Temporary = subscriber.Temporary,
                    ReceiveOn = subscriber.ReceiveOn,
                    // The next-fire time of the other scheduled type. Left out, an aggregation
                    // row had no next run to show at all — ReceiveOn is only ever set for
                    // Receiving, and the list page reads one field for both.
                    AggregateOn = subscriber.AggregateOn,
                    AggregationTarget = subscriber.AggregationTarget,
                    PausedOn = subscriber.PausedOn,
                    IsRunning = subscriber.IsRunning,
                    ConsecutiveFailures = subscriber.ConsecutiveFailures,
                    LastException = subscriber.LastException,
                    RetryPolicyId = subscriber.RetryPolicyId,
                    MapperProperties = subscriber.MapperProperties.ToKeyAndValueCollection(),
                    HandlerProperties = subscriber.HandlerProperties.ToKeyAndValueCollection(),
                    ReceiverProperties = subscriber.ReceiverProperties.ToKeyAndValueCollection(),
                    ValidatorProperties = subscriber.ValidatorProperties.ToKeyAndValueCollection(),
                    DocumentFilter = subscriber.DocumentFilter.ToKeyAndValueCollection(),
                    MatchExpression = subscriber.MatchExpression,
                    PartnerId = subscriber.PartnerId,
                    CategoryId = subscriber.CategoryId,
                    WorkGroupId =  subscriber.WorkGroupId,
                    CategoryDescription = subscriber.Category.Description,
                    CategoryCode = subscriber.Category.Code,
                    CustomRetryPolicy = subscriber.CustomRetryPolicy,
                    ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
                    ResponseMessageTypeName = subscriber.ResponseMessageTypeName,
                };

            query = query.AsNoTracking().AsQueryable();

            var edgeCaseFilters = HandleSearchyEdgeCases(searchyRequest.Conditions);

            if (lookup)
            {
                return await query.OrderBy(s => s.Name)
                    .Search(searchyRequest.Conditions)
                    .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);
            }


            var count = await query.Search(searchyRequest.Conditions).CountAsync();

            if (edgeCaseFilters.Any())
            {
                return await SearchWithEdgeCases(query, edgeCaseFilters, searchyRequest);
            }

            var result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize,
                searchyRequest.PageIndex).ToListAsync();
            await AttachSchedules(result);
            StripAdapterProperties(result);

            return new SearchyResponse<SubscriptionSearch>
            {
                TotalCount = count,
                Result = result
            };
        }

        /// <summary>Empties the adapter property collections on every returned row.</summary>
        /// <remarks>
        /// Adapter properties hold credentials — a storage key, an OAuth secret, the password for a
        /// partner system. <c>Subscriptions/Get</c> replaces those with a sentinel before answering;
        /// this list endpoint returned them verbatim, which put every integration's secrets one
        /// request away from anyone allowed to see the list at all.
        /// <para>
        /// Emptied here rather than left out of the projection because the projection needs them:
        /// the <c>rawsubscriptionproperties</c> filter searches inside their values, in memory, over
        /// the rows this query produced. So they are loaded, used, and then dropped before the
        /// response is shaped. Nothing reads them off this endpoint — both UIs configure a
        /// subscription from <c>Subscriptions/Get</c>, and the list has never shown them.
        /// </para>
        /// <para>
        /// Emptied rather than masked because masking can only hide what an adapter declares
        /// <c>[Secure]</c>, and the external adapters declare nothing — masking here would have
        /// looked like a fix while leaving those integrations exactly as exposed.
        /// </para>
        /// </remarks>
        private static void StripAdapterProperties(List<SubscriptionSearch> rows)
        {
            foreach (var row in rows)
            {
                row.HandlerProperties = [];
                row.MapperProperties = [];
                row.ReceiverProperties = [];
                row.ValidatorProperties = [];
            }
        }

        /// <summary>Fills in each returned row's schedules.</summary>
        /// <remarks>
        /// A second query rather than part of the projection above: <c>Schedule.On</c> is a
        /// <see cref="System.TimeSpan"/> stored as ticks, and reading <c>.Days</c>/<c>.Hours</c>/
        /// <c>.Minutes</c> off it inside that joined query is what Postgres cannot translate — a
        /// date_part type mismatch. Read flat for the ids actually being returned and shaped in
        /// memory, which asks nothing of the translator, so the list can finally say when a job
        /// runs instead of leaving its schedule column blank.
        /// </remarks>
        private async Task AttachSchedules(List<SubscriptionSearch> rows)
        {
            // Only the two scheduled types have any; asking for the rest is a wasted round trip.
            var ids = rows
                .Where(r => r.Type is SubscriptionType.Receiving or SubscriptionType.Aggregation)
                .Select(r => r.Id)
                .ToList();

            if (ids.Count == 0) return;

            var byId = await _dbContext.Set<Subscription>().AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, Schedules = s.Schedules.ToList() })
                .ToDictionaryAsync(x => x.Id, x => x.Schedules);

            foreach (var row in rows)
            {
                if (!byId.TryGetValue(row.Id, out var schedules)) continue;

                row.Schedules = schedules.Select(s => new ScheduleView
                {
                    Backwards = s.Backwards,
                    Recurrence = s.Recurrence,
                    Days = s.On.Days,
                    Hours = s.On.Hours,
                    Minutes = s.On.Minutes
                }).ToList();
            }
        }

        private async Task<SearchyResponse<SubscriptionSearch>> SearchWithEdgeCases(
            IQueryable<SubscriptionSearch> query, IEnumerable<SearchyFilter> edgeCaseFilters,
            SearchyRequest searchyRequest)
        {
            var data = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts).ToListAsync();

            foreach (var edgeCaseFilter in edgeCaseFilters)
            {
                var searchTerm = edgeCaseFilter.ValueString.ToLower();

                data = edgeCaseFilter.Field.ToLower() switch
                {
                    "rawsubscriptionproperties" => data.Where(i =>
                            i.HandlerProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                            i.ReceiverProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                            i.MapperProperties.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                            i.ValidatorProperties.Any(p => p.Value.ToLower().Contains(searchTerm)))
                        .ToList(),
                    "rawfiltersproperties" => data.Where(i =>
                            i.DocumentFilter.Any(p => p.Value.ToLower().Contains(searchTerm)) ||
                            (i.MatchExpression?.ToString()?.Contains(searchTerm) ?? false))
                        .ToList(),
                    "name" => data.Where(i => i.Name.ToLower().Contains(searchTerm))
                        .ToList(),
                    _ => data
                };
            }

            // Only the page being returned, same as the normal path — the edge-case filters run
            // over the whole set in memory, and shaping schedules for all of it would be waste.
            var page = data.Skip(searchyRequest.PageSize * searchyRequest.PageIndex)
                .Take(searchyRequest.PageSize).ToList();
            await AttachSchedules(page);
            StripAdapterProperties(page);

            return new SearchyResponse<SubscriptionSearch>
            {
                TotalCount = data.Count,
                Result = page
            };
        }


        private ICollection<SearchyFilter> HandleSearchyEdgeCases(ICollection<SearchyCondition> filters)
        {
            var list = new List<SearchyFilter>();
            if (filters is null || filters.Count == 0)
                return list;

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