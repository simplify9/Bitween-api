using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Xchanges
{
    public class Search : ISearchyHandler
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;
        private readonly XchangeService xchangeService;

        public Search(BitweenDbContext dbContext, XchangeService xchangeService, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
            this.xchangeService = xchangeService;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View, Model.Permissions.Dashboard.View);

            searchyRequest.DatesToUtc();
            await using var dr = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted);

            var query = from xchange in dbContext.Set<Xchange>()
                        join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                        from result in xr.DefaultIfEmpty()
                        join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                        from agg in xa.DefaultIfEmpty()
                        join promoted in dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
                        from promoted in xp.DefaultIfEmpty()
                        join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                        join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
                        from subscriber in xs.DefaultIfEmpty()
                        join delayedRetry in dbContext.Set<DelayedRetry>() on xchange.Id equals delayedRetry.Id into drGroup
                        from delayedRetry in drGroup.DefaultIfEmpty()
                        select new XchangeRow
                        {
                            Id = xchange.Id,
                            HandlerId = xchange.HandlerId,
                            MapperId = xchange.MapperId,
                            DocumentId = xchange.DocumentId,
                            DocumentName = document.Name,
                            StartedOn = xchange.StartedOn,
                            FinishedOn = result.FinishedOn,
                            AggregatedOn = agg.AggregatedOn,
                            SubscriptionId = xchange.SubscriptionId,
                            SubscriptionName = subscriber.Name,
                            Status = result.Success,
                            InputUrl = xchangeService.GetFileUrl(xchange.Id, xchange.InputSize, XchangeFileType.Input),
                            OutputUrl = xchangeService.GetFileUrl(xchange.Id, result.OutputSize, XchangeFileType.Output),
                            ResponseUrl = xchangeService.GetFileUrl(xchange.Id, result.ResponseSize, XchangeFileType.Response),
                            InputKey = xchangeService.GetFileKey(xchange.Id, xchange.InputSize, XchangeFileType.Input),
                            OutputKey = xchangeService.GetFileKey(xchange.Id, result.OutputSize, XchangeFileType.Output),
                            ResponseKey = xchangeService.GetFileKey(xchange.Id, result.ResponseSize, XchangeFileType.Response),
                            Duration = xchange.StartedOn.Elapsed(result.FinishedOn),
                            PromotedProperties = promoted == null ? null : promoted.Properties.ToDictionary(),
                            PromotedPropertiesRaw = promoted == null ? null : promoted.PropertiesRaw,
                            RetryFor = xchange.RetryFor,
                            AggregationXchangeId = agg.AggregationXchangeId,
                            Exception = result.Exception,
                            OutputBad = result.OutputBad,
                            ResponseBad = result.ResponseBad,
                            References = xchange.References,
                            InputFileName = xchange.InputName,
                            OutputFileName = result.OutputName,
                            ResponseFileName = result.ResponseName,
                            CorrelationId = xchange.CorrelationId,
                            // xchange.PartnerId is the authoritative source (set at creation from the
                            // gateway/bus-route partner, or the subscription's own PartnerId as a
                            // fallback there too) but the column was added later with no backfill, so
                            // pre-migration xchanges have it null even when their subscription carries
                            // a direct PartnerId — fall back to that for those legacy rows.
                            PartnerId = xchange.PartnerId ?? subscriber.PartnerId,
                            ScheduledRetryOn = delayedRetry != null ? delayedRetry.On : (DateTime?)null,
                            RetryBlockedReason = result.RetryBlockedReason
                        };

            var condition = searchyRequest.Conditions.FirstOrDefault();
            if (condition != null)
            {
                var idFilters = condition.Filters.Where(f => f.Field == "Id").ToList();
                foreach (var idFilter in idFilters)
                {
                    var value = idFilter.Value.ToString();
                    switch (idFilter.Rule)
                    {
                        case SearchyRule.EqualsTo:
                            query = query.Where(i =>
                                i.Id == value || i.RetryFor == value || i.AggregationXchangeId == value);
                            break;
                        case SearchyRule.Contains:
                            {
                                var valueAsArray = idFilter.ValueStringArray;
                                query = query.Where(i =>
                                    valueAsArray.Any(v => i.RetryFor == v) ||
                                    valueAsArray.Any(v => i.AggregationXchangeId == v) ||
                                    valueAsArray.Any(v => i.Id == v)
                                );
                                break;
                            }


                        default:
                            throw new SWValidationException("NOT_SUPPORTED", "Search query not supported");
                    }

                    condition.Filters.Remove(idFilter);
                }

                var statusFilters = condition.Filters.Where(f => f.Field == "StatusFilter").ToList();
                foreach (var statusFilter in statusFilters)
                {
                    switch (statusFilter.Value)
                    {
                        case "0":
                            // "Still running" means no result row exists yet. Asking for it as
                            // Status == null reads as `x0.success IS NULL` on the left join, and
                            // Postgres cannot estimate that: it guesses one row, plans every join
                            // above it for one row, and picks per-row sequential scans of the small
                            // side tables. Measured on 1M exchanges that was 22.8s for 25 rows.
                            // NOT EXISTS asks the same question as an anti-join, which it can
                            // estimate — 34ms. Equivalent because success is NOT NULL, so a result
                            // row can never itself carry a null status.
                            query = query.Where(i =>
                                !dbContext.Set<XchangeResult>().Any(r => r.Id == i.Id));
                            break;
                        case "1":
                            query = query.Where(i => i.Status == true && i.ResponseBad != true);
                            break;

                        case "2":
                            query = query.Where(i => i.Status == true && i.ResponseBad == true);
                            break;

                        case "3":
                            query = query.Where(i => i.Status == false);
                            break;
                    }

                    condition.Filters.Remove(statusFilter);
                }

                var propertiesFilters = condition.Filters
                    .Where(f => f.Field == "PromotedPropertiesRaw").ToList();
                foreach (var propertyFilter in propertiesFilters)
                {
                    var value = propertyFilter.Value.ToString()!.ToLower();

                    // Both sides lower-cased at query time. Promoted values keep the case the
                    // payload had (see FilterService), so the column has to be folded here for
                    // the search to stay case-insensitive. No index is lost: a Contains is a
                    // leading-wildcard LIKE, which the b-tree on this column could never serve.
                    query = query.Where(i => i.PromotedPropertiesRaw.ToLower().Contains(value));
                    condition.Filters.Remove(propertyFilter);
                }
            }

            var s = query.OrderByDescending(p => p.StartedOn).AsNoTracking().Search(searchyRequest.Conditions,
                searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            var r = await s.ToListAsync();

            var searchyResponse = new SearchyResponse<XchangeRow>
            {
                Result = r,
                TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync()
            };

            return searchyResponse;
        }
    }
}