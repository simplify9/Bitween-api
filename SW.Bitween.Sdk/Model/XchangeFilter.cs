using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SW.Bitween.Model;

/// <summary>
/// Builds the exchange search's filters, each one <c>Field:Rule:Value</c>, from typed choices. The
/// one place they're written in C#: the client SDK's queries and Bitween's own MCP tools both use it,
/// so a filter reads the same wherever it comes from. What is left unset matches any.
/// </summary>
public static class XchangeFilter
{
    /// <param name="status">0 processing, 1 success, 2 bad response, 3 failed, as the Exchanges page's tabs.</param>
    /// <param name="ids">These exchanges, with each one's retry and the exchanges it rolled up.</param>
    /// <param name="promotedProperties">By key: the value contains the text, or with no text, the key was promoted at all.</param>
    /// <param name="isRetry">True for retries only, false for first attempts only.</param>
    /// <param name="hasScheduledRetry">True for those with an automatic retry waiting, false for those without.</param>
    public static List<string> Build(
        int? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        IReadOnlyList<int>? partnerIds = null,
        IReadOnlyList<int>? subscriptionIds = null,
        IReadOnlyList<int>? informationTypeIds = null,
        IReadOnlyList<string>? ids = null,
        string? retryFor = null,
        string? correlationId = null,
        bool latestOnly = false,
        string? errorContains = null,
        IEnumerable<KeyValuePair<string, string?>>? promotedProperties = null,
        bool? isRetry = null,
        bool? hasScheduledRetry = null)
    {
        var filters = new List<string>();
        // Rule 1 is "is"; 9 is "is any of", with its values typed and joined.
        void OneOf(string field, IReadOnlyList<int>? values)
        {
            if (values is not { Count: > 0 }) return;
            filters.Add(values.Count == 1 ? $"{field}:1:{values[0]}" : $"{field}:9:numeric|{string.Join("|", values)}");
        }

        // On only for true; rule 2 turns it round.
        void Flag(string field, bool? value)
        {
            if (value != null) filters.Add($"{field}:{(value.Value ? 1 : 2)}:true");
        }

        if (status != null) filters.Add($"StatusFilter:1:{status}");
        OneOf("PartnerId", partnerIds);
        OneOf("SubscriptionId", subscriptionIds);
        OneOf("DocumentId", informationTypeIds);
        if (ids is { Count: > 0 }) filters.Add($"Id:4:text|{string.Join("|", ids)}");
        if (!string.IsNullOrWhiteSpace(retryFor)) filters.Add($"RetryFor:1:{retryFor.Trim()}");
        if (!string.IsNullOrWhiteSpace(correlationId)) filters.Add($"CorrelationId:1:{correlationId.Trim()}");
        if (latestOnly) filters.Add("LatestOnly:1:true");
        Flag("IsRetry", isRetry);
        Flag("HasScheduledRetry", hasScheduledRetry);
        if (!string.IsNullOrWhiteSpace(errorContains)) filters.Add($"ErrorContains:4:{errorContains.Trim()}");
        foreach (var (key, text) in promotedProperties ?? [])
            filters.Add(string.IsNullOrWhiteSpace(text) ? $"PromotedProperty:1:{key}" : $"PromotedProperty:4:{key}:{text.Trim()}");
        // Rules 6 and 8, at or after and at or before: the range rule, 21, can't be sent a time
        // Postgres will compare (the Exchanges page found the same).
        if (from != null) filters.Add($"StartedOn:6:{Time(from.Value)}");
        if (to != null) filters.Add($"StartedOn:8:{Time(to.Value)}");
        return filters;
    }

    /// <summary>The filters for the scheduled retries search, as the Scheduled retries page filters them.</summary>
    /// <param name="from">Due at or after this.</param>
    /// <param name="to">Due at or before this.</param>
    public static List<string> ScheduledRetries(
        int? subscriptionId = null,
        int? informationTypeId = null,
        string? errorContains = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var filters = new List<string>();
        if (subscriptionId != null) filters.Add($"SubscriptionId:1:{subscriptionId}");
        if (informationTypeId != null) filters.Add($"DocumentId:1:{informationTypeId}");
        if (!string.IsNullOrWhiteSpace(errorContains)) filters.Add($"Exception:4:{errorContains.Trim()}");
        if (from != null) filters.Add($"On:6:{Time(from.Value)}");
        if (to != null) filters.Add($"On:8:{Time(to.Value)}");
        return filters;
    }

    /// <summary>Filters, paging and the rest as the search's query string, every value escaped.</summary>
    public static string QueryString(IEnumerable<string> filters, params (string Name, string Value)[] more) =>
        string.Join("&", filters.Select(f => $"filter={Uri.EscapeDataString(f)}")
            .Concat(more.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}")));

    /// <summary>In UTC to the tick, and on the Gregorian calendar whatever the caller's culture uses.</summary>
    public static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
