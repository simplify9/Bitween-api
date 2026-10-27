#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.Services.Ai;

/// <summary>A partner, subscription or information type, as the model may see it: an id and a name.</summary>
public record QueryName(int Id, string Name, string? Detail = null);

/// <summary>Everything a question may name, and the only things a filter may hold.</summary>
public record QueryNames(
    IReadOnlyList<QueryName> Partners,
    IReadOnlyList<QueryName> Subscriptions,
    IReadOnlyList<QueryName> InformationTypes,
    IReadOnlyList<string> PropertyKeys);

/// <summary>A question turned into an Exchanges page filter.</summary>
/// <param name="Query">The page's own URL parameters, so the filter is the page's chips and its link.</param>
/// <param name="Answer">A sentence for the page to show, with <c>{count}</c> where the page's total goes.</param>
/// <param name="Notes">What the filter couldn't say: parts of the question, or names that don't exist.</param>
public record ExchangeQuery(string Query, string Answer, IReadOnlyList<string> Notes);

/// <summary>
/// Turns a plain-English question about exchanges into the Exchanges page's filter.
/// </summary>
/// <remarks>
/// <para>
/// The model sees the question, the time, and names: of partners, subscriptions and information
/// types, and promoted-property keys. Never an exchange, a count, or a property's value - the count
/// in the answer is the page's, filled in where the model leaves <c>{count}</c>.
/// </para>
/// <para>
/// The answer is a filter in the model's own small shape rather than a URL, and every part of it is
/// checked against <see cref="QueryNames"/> before it becomes one: an id that doesn't exist or a key
/// no information type promotes is dropped and said so, never passed on to match nothing silently.
/// </para>
/// </remarks>
public static class ExchangeQueryPrompt
{
    /// <summary>How many of each kind of name are sent; the rest are left out, alphabetically last.</summary>
    public const int MaxNames = 2_000;

    public const int MaxQuestionLength = 500;

    const int MaxText = 200;
    const int MaxAnswer = 300;

    public const string Instructions = """
        You turn a question about exchanges in Bitween, an integration platform, into a filter for
        its Exchanges page.

        An exchange is one document passing through Bitween for one subscription: received, mapped,
        then delivered by a handler. A subscription belongs to a partner and carries one information
        type (a kind of document, such as shipment orders). An exchange's status is one of:
        - "failed": it failed.
        - "badResponse": it was delivered, but the partner's response was marked as bad.
        - "processing": not finished yet.
        - "success": delivered fine.
        Promoted properties are values Bitween copies out of a document so it can be found by them,
        such as an order number; each has a key.

        You get the question, the current time in the asker's time zone, and the partners,
        subscriptions and information types by id and name, with the promoted-property keys. You
        never see exchanges or counts.

        Answer with one JSON object and nothing else. Leave out or set to null anything the question
        doesn't ask for:
        {
          "status": "failed" | "badResponse" | "processing" | "success",
          "subscriptionIds": [ids], "partnerIds": [ids], "informationTypeIds": [ids],
          "properties": [{ "key": "a promoted-property key", "text": "text in its value, or null for any value", "not": false }],
          "anyProperty": "text to find in any promoted property",
          "error": "text to find in the error message",
          "correlationId": "an exact correlation id",
          "range": "15m" | "1h" | "24h" | "7d" | "30d" | "today" | "yesterday" | "<number>m" | "<number>h" | "<number>d",
          "from": "YYYY-MM-DDTHH:mm", "to": "YYYY-MM-DDTHH:mm",
          "latest": true, "retry": true, "scheduled": true, "aggregated": true,
          "not": ["subscription" | "partner" | "informationType" | "property" | "error" | "retry" | "scheduled" | "aggregated"],
          "answer": "one short sentence describing the result, with {count} where the number goes",
          "notUnderstood": "the part of the question the filter can't express, or null"
        }

        Rules:
        - Use only ids from the lists. Match names loosely - "skynet" is the partner "SkyNet Express" -
          and when a name fits a partner, filter by partner rather than listing its subscriptions.
        - Several ids of one kind mean any of them. All filters together must all hold.
        - "not" turns the named filters round: "not": ["partner"] means every partner except those.
        - For a time like "since Monday" or "on 3 October", use from and to in the asker's time zone.
          For "the last 2 hours", "today" or "yesterday", use range. Never both.
        - "latest": only the latest attempt of each exchange, leaving out ones that were retried. Use
          it for "still failing", "unresolved" or "needs attention".
        - "retry": the exchange is a retry of an earlier one. "scheduled": a retry is scheduled.
          "aggregated": the exchange was gathered into an aggregation.
        - For a count question ("how many ..."), still return the filter; the page counts it.
        - The answer is shown above the result, so write it as a result, not a reply:
          "{count} failed SkyNet exchanges since Monday", never "Here are...".
        - If the question isn't about exchanges at all, return only "answer" and "notUnderstood".
        """;

    /// <summary>The message for one question.</summary>
    public static string Message(string question, DateTime now, string timeZone, QueryNames names)
    {
        var message = new StringBuilder();
        message.AppendLine($"Now: {now:yyyy-MM-dd HH:mm}, {now:dddd}, in {timeZone}.");
        message.AppendLine();
        message.AppendLine("<question>");
        message.AppendLine(question.Trim());
        message.AppendLine("</question>");
        List("partners", names.Partners, message);
        List("subscriptions", names.Subscriptions, message);
        List("information_types", names.InformationTypes, message);
        message.AppendLine();
        message.AppendLine("<promoted_property_keys>");
        message.AppendLine(names.PropertyKeys.Count == 0 ? "(none)" : string.Join(", ", names.PropertyKeys));
        message.AppendLine("</promoted_property_keys>");
        return message.ToString();
    }

    static void List(string tag, IReadOnlyList<QueryName> names, StringBuilder message)
    {
        message.AppendLine();
        message.AppendLine($"<{tag}>");
        foreach (var name in names.Take(MaxNames))
            message.AppendLine(name.Detail is null ? $"{name.Id}: {name.Name}" : $"{name.Id}: {name.Name} ({name.Detail})");
        if (names.Count == 0) message.AppendLine("(none)");
        if (names.Count > MaxNames) message.AppendLine($"({names.Count - MaxNames} more not listed)");
        message.AppendLine($"</{tag}>");
    }

    static readonly string[] Statuses = ["failed", "badResponse", "processing", "success"];
    static readonly string[] Negatable = ["subscription", "partner", "informationType", "property", "error", "retry", "scheduled", "aggregated"];
    static readonly Regex Range = new(@"^(today|yesterday|[1-9]\d{0,3}[mhd])$", RegexOptions.Compiled);
    static readonly Regex Bound = new(@"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2})?$", RegexOptions.Compiled);

    /// <summary>The model's answer as the page's filter, keeping only what <paramref name="names"/> holds.</summary>
    public static ExchangeQuery Parse(string answer, QueryNames names)
    {
        var notes = new List<string>();
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        JObject? json = null;
        if (start >= 0 && end > start)
        {
            try
            {
                json = JObject.Parse(answer[start..(end + 1)]);
            }
            catch (JsonException)
            {
            }
        }
        if (json is null)
            return new ExchangeQuery("", "", ["The AI's answer couldn't be read as a filter. Try asking again."]);

        var query = new List<(string Key, string Value)>();
        var set = new HashSet<string>();

        if (Text(json, "status") is { } status)
        {
            var known = Statuses.FirstOrDefault(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));
            if (known is null) notes.Add($"Status \"{status}\" isn't one the page has.");
            else query.Add(("status", known));
        }

        Ids(json, "subscriptionIds", "subscriptionId", "subscription", names.Subscriptions, query, set, notes);
        Ids(json, "partnerIds", "partnerId", "partner", names.Partners, query, set, notes);
        Ids(json, "informationTypeIds", "informationTypeId", "informationType", names.InformationTypes, query, set, notes);

        if (Text(json, "correlationId") is { } correlation) query.Add(("correlationId", correlation));

        foreach (var property in json["properties"] as JArray ?? [])
        {
            if (property is not JObject p || Text(p, "key") is not { } key) continue;
            var known = names.PropertyKeys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                notes.Add($"No information type promotes a property \"{key}\".");
                continue;
            }
            var text = Text(p, "text");
            query.Add(("prop", $"{(Flag(p, "not") ? "!" : "")}{known}{(text is null ? "" : $":{text}")}"));
        }
        if (Text(json, "anyProperty") is { } anyProperty)
        {
            query.Add(("property", anyProperty));
            set.Add("property");
        }

        if (Text(json, "error") is { } error)
        {
            query.Add(("error", error));
            set.Add("error");
        }

        foreach (var flag in new[] { "retry", "scheduled", "aggregated" })
            if (Flag(json, flag))
            {
                query.Add((flag, "1"));
                set.Add(flag);
            }
        if (Flag(json, "latest")) query.Add(("latest", "1"));

        var range = Text(json, "range")?.ToLowerInvariant();
        var from = Text(json, "from");
        var to = Text(json, "to");
        if (range is not null && Range.IsMatch(range))
        {
            query.Add(("range", range));
        }
        else
        {
            if (range is not null) notes.Add($"\"{range}\" isn't a time range the page has.");
            if (from is not null && Bound.IsMatch(from)) query.Add(("from", from));
            if (to is not null && Bound.IsMatch(to)) query.Add(("to", to));
        }

        // Only filters that are set can be turned round; the page ignores the rest anyway.
        var not = (json["not"] as JArray ?? [])
            .Select(n => n.Type == JTokenType.String ? Negatable.FirstOrDefault(k => k.Equals((string?)n, StringComparison.OrdinalIgnoreCase)) : null)
            .Where(n => n is not null && set.Contains(n))
            .Distinct().Order().ToList();
        if (not.Count > 0) query.Add(("not", string.Join(",", not)));

        if (Text(json, "notUnderstood") is { } notUnderstood) notes.Insert(0, notUnderstood);

        var sentence = Text(json, "answer", MaxAnswer) ?? "";
        return new ExchangeQuery(
            string.Join("&", query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}")),
            sentence, notes);
    }

    static void Ids(JObject json, string field, string parameter, string kind, IReadOnlyList<QueryName> known,
        List<(string, string)> query, HashSet<string> set, List<string> notes)
    {
        var ids = new List<int>();
        foreach (var token in json[field] as JArray ?? [])
        {
            if (token.Type != JTokenType.Integer) continue;
            var id = (int)token;
            if (known.Any(n => n.Id == id)) ids.Add(id);
            else notes.Add($"There is no {Words(kind)} with id {id}.");
        }
        if (ids.Count == 0) return;
        query.Add((parameter, string.Join(",", ids.Distinct())));
        set.Add(kind);
    }

    static string Words(string kind) => kind == "informationType" ? "information type" : kind;

    static string? Text(JObject json, string field, int max = MaxText)
    {
        if (json[field] is not { Type: JTokenType.String } token) return null;
        var text = ((string?)token)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }

    static bool Flag(JObject json, string field) => json[field] is { Type: JTokenType.Boolean } token && (bool)token;
}
