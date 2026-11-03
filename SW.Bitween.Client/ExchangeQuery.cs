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
    /// Promoted properties by key: the value contains the text, ignoring case, or with a null text,
    /// the exchange promoted the key at all. Every one has to hold.
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
        var filters = new List<string>();
        // Rule 1 is "is"; 9 is "is any of", with its values typed and joined.
        void OneOf(string field, IReadOnlyList<int>? values)
        {
            if (values is not { Count: > 0 }) return;
            filters.Add(values.Count == 1 ? $"{field}:1:{values[0]}" : $"{field}:9:numeric|{string.Join("|", values)}");
        }

        if (Status != null) filters.Add($"StatusFilter:1:{(int)Status}");
        OneOf("PartnerId", PartnerIds);
        OneOf("SubscriptionId", SubscriptionIds);
        OneOf("DocumentId", InformationTypeIds);
        if (Ids is { Count: > 0 }) filters.Add($"Id:4:text|{string.Join("|", Ids)}");
        if (!string.IsNullOrWhiteSpace(RetryFor)) filters.Add($"RetryFor:1:{RetryFor.Trim()}");
        if (!string.IsNullOrWhiteSpace(CorrelationId)) filters.Add($"CorrelationId:1:{CorrelationId.Trim()}");
        if (LatestOnly) filters.Add("LatestOnly:1:true");
        if (!string.IsNullOrWhiteSpace(ErrorContains)) filters.Add($"ErrorContains:4:{ErrorContains.Trim()}");
        foreach (var (key, text) in PromotedProperties ?? new Dictionary<string, string?>())
            filters.Add(string.IsNullOrWhiteSpace(text) ? $"PromotedProperty:1:{key}" : $"PromotedProperty:4:{key}:{text.Trim()}");
        // Rules 6 and 8, at or after and at or before: the range rule, 21, can't be sent a time
        // Postgres will compare (the Exchanges page found the same).
        if (From != null) filters.Add($"StartedOn:6:{Time(From.Value)}");
        if (To != null) filters.Add($"StartedOn:8:{Time(To.Value)}");
        filters.AddRange(Filters);
        return filters;
    }

    internal static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Filters, paging and the rest as a query string, every value escaped.</summary>
    internal static string QueryString(IEnumerable<string> filters, params (string Name, string Value)[] more) =>
        string.Join("&", filters.Select(f => $"filter={Uri.EscapeDataString(f)}")
            .Concat(more.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}")));
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

    internal List<string> ToFilters()
    {
        var filters = new List<string>();
        if (SubscriptionId != null) filters.Add($"SubscriptionId:1:{SubscriptionId}");
        if (InformationTypeId != null) filters.Add($"DocumentId:1:{InformationTypeId}");
        if (!string.IsNullOrWhiteSpace(ErrorContains)) filters.Add($"Exception:4:{ErrorContains.Trim()}");
        if (From != null) filters.Add($"On:6:{ExchangeQuery.Time(From.Value)}");
        if (To != null) filters.Add($"On:8:{ExchangeQuery.Time(To.Value)}");
        return filters;
    }
}

/// <summary>One page of a search, and how many match in all.</summary>
public sealed class Page<T>
{
    public List<T> Result { get; set; } = [];

    /// <summary>How many match. An exchange search stops counting at 10,001, meaning more than 10,000.</summary>
    public int TotalCount { get; set; }
}
