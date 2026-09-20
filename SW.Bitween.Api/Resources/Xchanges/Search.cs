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
    public class Search(BitweenDbContext dbContext, XchangeService xchangeService,
        RequestContext requestContext) : ISearchyHandler
    {
        /// <summary>
        /// Largest exact total the exchange search reports. Beyond it the response carries
        /// <c>CountCap + 1</c>, meaning "more than this" — the client renders that as "10,000+".
        /// Kept in step with <c>COUNT_CAP</c> in ClientApp's <c>ExchangesPage.tsx</c>.
        /// </summary>
        internal const int CountCap = 10_000;

        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;
        private readonly XchangeService xchangeService = xchangeService;

        public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            // Lookup returns only id/name pairs, which pickers across the app rely on;
            // the full list is the data, so that's what the view permission covers.
            if (!lookup)
                await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View, Model.Permissions.Dashboard.View);

            searchyRequest.DatesToUtc();
            await XchangeFilters.ResolveReceiveAttemptFilterAsync(searchyRequest, dbContext);
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
                            // The same three counts the file keys above are already derived from.
                            // Left unassigned, every stage reported its document as "0 b".
                            //
                            // The result-side two are guarded because the join to XchangeResult is
                            // a left one: an exchange still running, or one that failed before it
                            // produced a result, has no row there. Reading a non-nullable int off
                            // that null threw "Nullable object must have a value" out of the whole
                            // query — one such exchange 500'd the entire list.
                            InputFileSize = xchange.InputSize,
                            OutputFileSize = result != null ? result.OutputSize : 0,
                            ResponseFileSize = result != null ? result.ResponseSize : 0,
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

            query = query.ApplySpecialFilters(searchyRequest, dbContext);

            var s = query.OrderByDescending(p => p.StartedOn).AsNoTracking().Search(searchyRequest.Conditions,
                searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex);

            var r = await s.ToListAsync();

            // Which of these have already been retried, so the client can tell a spent exchange
            // from a retryable one without asking about each row's chain. Asked separately rather
            // than as a subquery in the projection above, because TotalCount reuses that query and
            // an exact count already costs more than fetching the rows does — this way neither
            // plan changes. One indexed lookup over RetryFor for the page's worth of ids.
            if (r.Count > 0)
            {
                var pageIds = r.Select(row => row.Id).ToList();
                var retriedIds = await dbContext.Set<Xchange>().AsNoTracking()
                    .Where(x => pageIds.Contains(x.RetryFor))
                    .Select(x => x.RetryFor)
                    .Distinct()
                    .ToListAsync();
                foreach (var row in r)
                    row.HasRetry = retriedIds.Contains(row.Id);
            }

            var searchyResponse = new SearchyResponse<XchangeRow>
            {
                Result = r,
                // Counting every match is what a filtered search now spends its time on: the rows
                // themselves come back in a few milliseconds, while an exact count has to visit
                // every matching row because it cannot stop early. Measured on 1M exchanges, the
                // Success pill's count was 264ms against 0.5ms for the rows.
                //
                // Stop counting past the cap and report the cap + 1 instead, which the client shows
                // as "10,000+". 36ms drops to 2.3ms unfiltered, 264ms to 25ms on Success. Anything
                // filtered narrowly enough to act on still gets an exact number; only views far too
                // broad to page through lose it, and 10,000 rows is 400 pages of Next.
                TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions)
                    .Take(CountCap + 1).CountAsync()
            };

            return searchyResponse;
        }
    }
}