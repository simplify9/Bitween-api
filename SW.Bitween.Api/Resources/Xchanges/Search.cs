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
        private readonly XchangeService xchangeService;

        public Search(BitweenDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
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
                            PartnerId = subscriber.PartnerId,
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
                            query = query.Where(i => i.Status == null);
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

                    query = query.Where(i => i.PromotedPropertiesRaw.Contains(value));
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