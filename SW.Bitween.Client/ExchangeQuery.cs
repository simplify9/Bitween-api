namespace SW.Bitween.Client;

/// <summary>An exchange's status, as the Exchanges page's tabs divide them.</summary>
public enum ExchangeStatus
{
    /// <summary>Still running: no result yet.</summary>
    Processing = 0,

    /// <summary>Finished, and the response (if any) was not marked bad.</summary>
    Success = 1,

    /// <summary>Finished, but the response was marked bad.</summary>
    BadResponse = 2,

    Failed = 3,
}

/// <summary>
/// Which exchanges, filtered the way the Exchanges page filters them. What is left unset matches any;
/// everything set has to hold.
/// </summary>
public class ExchangeQuery
{
    public ExchangeStatus? Status { get; set; }

    /// <summary>Started at or after this.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Started at or before this.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Any of these partners'.</summary>
    public IReadOnlyList<int>? PartnerIds { get; set; }

    /// <summary>Run by any of these subscriptions.</summary>
    public IReadOnlyList<int>? SubscriptionIds { get; set; }

    /// <summary>Of any of these information types (documents, in the API).</summary>
    public IReadOnlyList<int>? InformationTypeIds { get; set; }

    /// <summary>
    /// These exchanges, with the retry of each and the exchanges each rolled up, as the Exchanges page's
    /// id search finds them. <see cref="Exchanges.GetAsync"/> is the one exchange alone.
    /// </summary>
    public IReadOnlyList<string>? Ids { get; set; }

    /// <summary>The retry of this exchange: an exchange has at most one.</summary>
    public string? RetryFor { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>Only the newest attempt of each retry chain.</summary>
    public bool LatestOnly { get; set; }

    /// <summary>The error contains this text, ignoring case.</summary>
    public string? ErrorContains { get; set; }

    /// <summary>
    /// Promoted properties by key: the value contains the text, ignoring case, or with a null or blank
    /// text, the exchange promoted the key at all. Every one has to hold.
    /// </summary>
    public IDictionary<string, string?>? PromotedProperties { get; set; }

    /// <summary>
    /// Filters in Bitween's own form, <c>Field:Rule:Value</c>, for what the properties above don't
    /// cover: <c>IsRetry:1:true</c>, say. See Exchanges in the API reference.
    /// </summary>
    public IList<string> Filters { get; } = new List<string>();

    /// <summary>The filters as Bitween's search takes them, each one <c>Field:Rule:Value</c>.</summary>
    internal List<string> ToFilters()
    {
        var filters = SW.Bitween.Model.XchangeFilter.Build(
            status: (int?)Status, from: From, to: To,
            partnerIds: PartnerIds, subscriptionIds: SubscriptionIds, informationTypeIds: InformationTypeIds,
            ids: Ids, retryFor: RetryFor, correlationId: CorrelationId, latestOnly: LatestOnly,
            errorContains: ErrorContains, promotedProperties: PromotedProperties);
        filters.AddRange(Filters);
        return filters;
    }

    internal static string Time(DateTimeOffset time) => SW.Bitween.Model.XchangeFilter.Time(time);

    /// <summary>Filters, paging and the rest as a query string, every value escaped.</summary>
    internal static string QueryString(IEnumerable<string> filters, params (string Name, string Value)[] more) =>
        SW.Bitween.Model.XchangeFilter.QueryString(filters, more);
}

/// <summary>
/// Which exchanges a bulk retry or an export covers: picked by id, or every one a query matches,
/// however many pages that comes to, with some left out.
/// </summary>
public sealed class ExchangeSelection
{
    ExchangeSelection(List<string>? ids, string? filter, List<string>? excludeIds)
    {
        Ids = ids;
        Filter = filter;
        ExcludeIds = excludeIds;
    }

    internal List<string>? Ids { get; }
    internal string? Filter { get; }
    internal List<string>? ExcludeIds { get; }

    /// <summary>These exchanges.</summary>
    public static ExchangeSelection Of(IEnumerable<string> ids) => new(ids.ToList(), null, null);

    /// <summary>Every exchange <paramref name="query"/> matches, but those in <paramref name="except"/>.</summary>
    public static ExchangeSelection Matching(ExchangeQuery query, IEnumerable<string>? except = null)
    {
        // An empty list adds no filter, so it would widen the selection rather than narrow it.
        if (query.Ids is { Count: 0 } || query.PartnerIds is { Count: 0 } || query.SubscriptionIds is { Count: 0 }
            || query.InformationTypeIds is { Count: 0 })
            throw new ArgumentException("An id list is empty: it would widen the selection, not narrow it.", nameof(query));
        var filters = query.ToFilters();
        // An empty filter reads as no filter, and the selection would fall back to its ids, which are
        // none. Every attempt, not only the newest, matches everything without narrowing it.
        if (filters.Count == 0) filters.Add("LatestOnly:1:false");
        return new(null, ExchangeQuery.QueryString(filters), except?.ToList());
    }
}

/// <summary>Which automatic retries, filtered the way the Scheduled retries page filters them.</summary>
public class ScheduledRetryQuery
{
    public int? SubscriptionId { get; set; }

    /// <summary>Of this information type (document, in the API).</summary>
    public int? InformationTypeId { get; set; }

    /// <summary>The error of the attempt it follows contains this text.</summary>
    public string? ErrorContains { get; set; }

    /// <summary>Due at or after this.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Due at or before this.</summary>
    public DateTimeOffset? To { get; set; }

    internal List<string> ToFilters() => SW.Bitween.Model.XchangeFilter.ScheduledRetries(
        SubscriptionId, InformationTypeId, ErrorContains, From, To);
}

/// <summary>One page of a search, and how many match in all.</summary>
public sealed class Page<T>
{
    public List<T> Result { get; set; } = [];

    /// <summary>
    /// How many match, or 0 when searched without counting. An exchange search stops counting at
    /// 10,001, meaning more than 10,000.
    /// </summary>
    public int TotalCount { get; set; }
}
