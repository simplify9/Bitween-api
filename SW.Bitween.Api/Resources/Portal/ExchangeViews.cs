using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// What the partner portal's Exchanges page reads: our team's own search, counts and retry chain,
/// limited to the caller's partner and without what is ours to know. The same rows, so the same page
/// shows them.
/// </summary>
internal static class PortalExchangeViews
{
    /// <summary>
    /// The filters and sorts the portal's page sends. Anything else is refused rather than ignored, so
    /// a hand-made request can't sort or filter by a column they aren't shown, such as an error they may
    /// not read, and learn it from the order or the count.
    /// </summary>
    private static readonly HashSet<string> Filterable = new(StringComparer.Ordinal)
    {
        nameof(XchangeRow.StartedOn), nameof(XchangeRow.SubscriptionId), nameof(XchangeRow.DocumentId),
        nameof(XchangeRow.CorrelationId), nameof(XchangeRow.PartnerId), "Id", "StatusFilter", "LatestOnly",
        "IsRetry", "HasScheduledRetry", "PromotedPropertyKey", nameof(XchangeRow.PromotedPropertiesRaw),
        "PromotedProperty"
    };

    private static readonly HashSet<string> Sortable = new(StringComparer.Ordinal)
    {
        nameof(XchangeRow.StartedOn), nameof(XchangeRow.FinishedOn)
    };

    public static void EnsureAllowed(SearchyRequest request, PortalCaller caller)
    {
        var withErrors = caller.Can(Model.Permissions.Portal.ExchangeErrorsView);
        foreach (var filter in request.Conditions.SelectMany(c => c.Filters))
            if (!Filterable.Contains(filter.Field) && !(withErrors && filter.Field == "ErrorContains"))
                throw new SWValidationException("FILTER_NOT_ALLOWED", $"The exchanges can't be filtered by {filter.Field}.");
        foreach (var sort in request.Sorts ?? [])
            if (!Sortable.Contains(sort.Field))
                throw new SWValidationException("SORT_NOT_ALLOWED", $"The exchanges can't be sorted by {sort.Field}.");
    }

    /// <summary>Takes out of a row what is ours to know, and what their role doesn't show.</summary>
    public static XchangeRow Strip(XchangeRow row, PortalCaller caller)
    {
        // How their exchange was wired, on our side: which adapters ran, what fed it, where it was rolled up.
        row.HandlerId = null;
        // Whether one mapped it is theirs to see (the page shows a skipped stage); which adapter isn't.
        row.MapperId = row.MapperId is null ? null : "mapper";
        row.References = null;
        row.SourceXchangeId = null;
        row.SourceValues = null;
        row.AggregationXchangeId = null;
        row.AggregatedOn = null;
        row.RetryBlockedReason = null;
        row.Warning = null;
        // Where storage keeps the files; the links below are what they open them with.
        row.InputKey = null;
        row.OutputKey = null;
        row.ResponseKey = null;
        row.InputFileHash = null;

        if (!caller.Can(Model.Permissions.Portal.ExchangeErrorsView))
            row.Exception = null;
        if (!caller.Can(Model.Permissions.Portal.ExchangeFilesView))
        {
            row.InputUrl = row.OutputUrl = row.ResponseUrl = null;
            row.InputFileName = row.OutputFileName = row.ResponseFileName = null;
            row.InputFileSize = row.OutputFileSize = row.ResponseFileSize = 0;
        }
        return row;
    }
}

/// <summary>Their exchanges, as our team's list shows exchanges: the same filters, rows and paging.</summary>
[HandlerName("xchangesearch")]
public class SearchExchanges(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        PortalExchangeViews.EnsureAllowed(searchyRequest, caller);
        var found = await new SW.Bitween.Resources.Xchanges.Search(dbContext, xchangeService, requestContext)
            .SearchAsync(searchyRequest, caller.PartnerId);
        foreach (var row in found.Result)
            PortalExchangeViews.Strip(row, caller);
        return found;
    }
}

/// <summary>How many of their exchanges a filter matches, by status: the counts on their Exchanges page.</summary>
[HandlerName("xchangesummary")]
public class CountExchanges(BitweenDbContext dbContext, RequestContext requestContext, SlowCounts slowCounts)
    : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        PortalExchangeViews.EnsureAllowed(searchyRequest, caller);
        return await new SW.Bitween.Resources.Xchanges.Summary(dbContext, requestContext, slowCounts).SummarizeAsync(searchyRequest, caller.PartnerId);
    }
}

/// <summary>The attempts of one of their exchanges, as our team's retry chain shows them.</summary>
[HandlerName("retrytree")]
public class ExchangeAttempts(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<XchangeRetryTreeRequest, object>
{
    public async Task<object> Handle(XchangeRetryTreeRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        // Someone else's exchange is simply not there for them.
        if (!await dbContext.Set<Xchange>().AnyAsync(x => x.Id == request.Id && x.PartnerId == caller.PartnerId))
            throw new SWNotFoundException(request.Id);

        var tree = await new SW.Bitween.Resources.Xchanges.RetryTree(dbContext, requestContext).TreeAsync(request, caller.PartnerId);
        var withErrors = caller.Can(Model.Permissions.Portal.ExchangeErrorsView);
        foreach (var node in tree.Nodes)
        {
            node.RetryBlockedReason = null;
            if (!withErrors) node.Exception = null;
        }
        // An original that wasn't theirs is left out, so the chain starts at their first attempt.
        if (tree.Nodes.All(n => n.Id != tree.RootId) && tree.Nodes.Count > 0)
            tree.RootId = tree.Nodes[0].Id;
        return tree;
    }
}
