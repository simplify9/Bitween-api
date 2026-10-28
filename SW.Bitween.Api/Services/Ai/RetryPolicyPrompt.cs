#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SW.Bitween.JsonConverters;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Ai;

/// <summary>One kind of failure a subscription had: the same first line, give or take its details.</summary>
/// <param name="Sample">The real first line and a little more - matched against here, never sent.</param>
/// <param name="Masked">What the model sees of it.</param>
/// <param name="Retried">How many of these were retried.</param>
/// <param name="FixedByRetry">How many of those retries succeeded - the mark of a passing glitch.</param>
public record FailureKind(string Sample, string Masked, int Count, int Retried, int FixedByRetry, DateTime FirstOn, DateTime LastOn);

/// <summary>What the model proposes: a named policy, why, and its groups.</summary>
public record RetrySuggestion(string Name, string Summary, List<RetryGroup> Groups);

/// <summary>
/// Proposes a retry policy from a subscription's real failures.
/// </summary>
/// <remarks>
/// <para>
/// The model sees each kind of failure as one masked line - the wording kept, values, addresses and
/// quoted text taken out - with how often it happened and how often a retry fixed it. Never a
/// document or a response body. The current policy's rules go too: they are configuration.
/// </para>
/// <para>
/// What comes back is checked as a saved policy is, then run against the real failures here, so
/// the person reviewing it sees what each one would do under it before anything is saved.
/// </para>
/// </remarks>
public static class RetryPolicyPrompt
{
    /// <summary>How many of the newest failures are looked at.</summary>
    public const int MaxFailures = 2_000;

    /// <summary>How many kinds of failure are sent, most frequent first.</summary>
    public const int MaxKinds = 15;

    /// <summary>How much of each error is kept to match against: enough for its exception types and message.</summary>
    public const int SampleLength = 4_000;

    public const string Instructions = """
        You design retry policies for Bitween, an integration platform. When an exchange fails,
        its subscription's retry policy decides whether to try it again, how often and how soon.

        A policy is a list of groups, checked in priority order (lowest first); the first group with
        a matching matcher decides. A failure no group matches is not retried.

        A group, as JSON:
        {
          "name": "short name",
          "priority": 0,
          "appliesTo": ["Error"],
          "matchers": [ ... at least one ... ],
          "action": "Allow" | "Block",
          "budget": {                         // required when action is Allow; null for Block
            "maxAttemptsPerError": 3,         // retries of one failing exchange
            "maxAttemptsTotal": 50,           // retries across all exchanges until the subscription next succeeds; null for no ceiling
            "delayStrategy": { "type": "exponential", "initialDelayMs": 30000, "multiplier": 2, "maxDelayMs": 600000 }
                          // or { "type": "fixed", "delayMs": 60000 }
                          // or { "type": "linear", "initialDelayMs": 30000, "incrementMs": 30000 }
          },
          "notes": "one sentence: which failures this is for and why it retries or stops them"
        }
        Matchers (any one matching is enough):
        - { "type": "contains", "value": "text", "caseSensitive": false }
        - { "type": "regex", "pattern": "a .NET regex", "flags": "i" }
        - { "type": "exceptionType", "value": "System.Net.Http.HttpRequestException", "includeInner": true }
        Matchers are checked against the whole error text: exception types, message and stack.

        You get the subscription's kinds of failure from recent weeks, most frequent first, each
        with a count and how often a retry fixed it, plus its current policy. Each error is masked:
        values became <text>, <number>, <email>, <query>. Match only on wording that survived
        masking - never on a <placeholder>, which is not in the real text.

        Guidance:
        - Retry what passes by itself: timeouts, refused or reset connections, 429, 502, 503, 504,
          failures that a retry has often fixed.
        - Don't retry what can't pass unchanged: mapping errors, validation errors, 400, 401, 403,
          404, malformed configuration such as an invalid URL. Leave them unmatched, or add a Block
          group when a broader Allow group would otherwise catch them.
        - Back off: exponential delays, a few attempts per exchange, a total that stops a long
          outage from hammering the partner.
        - Keep it small: two to four groups.

        Answer with one JSON object and nothing else:
        { "name": "policy name", "summary": "two sentences on what it does and why", "groups": [ ... ] }
        """;

    /// <summary>The message for one subscription.</summary>
    public static string Message(string subscription, int days, int failures, int badResponses,
        IReadOnlyList<FailureKind> kinds, IReadOnlyList<RetryGroup>? current)
    {
        var message = new StringBuilder();
        message.AppendLine(subscription);
        message.AppendLine($"Looked at: the last {days} days, {failures} failures" +
                           (failures >= MaxFailures ? $" (the newest {MaxFailures})" : "") + ".");
        if (badResponses > 0) message.AppendLine($"Also {badResponses} bad responses; their bodies are not sent.");
        message.AppendLine();
        message.AppendLine("<current_policy>");
        // The rules only: a group's alert settings name channels and, on old groups, carry handler properties.
        message.AppendLine(current is { Count: > 0 }
            ? JsonConvert.SerializeObject(current.Select(g => new { g.Name, g.Priority, g.Enabled, g.AppliesTo, g.Matchers, g.Action, g.Budget }), Settings)
            : "(none: nothing is retried)");
        message.AppendLine("</current_policy>");
        message.AppendLine();
        message.AppendLine("<failures>");
        var n = 0;
        foreach (var kind in kinds)
        {
            n++;
            message.AppendLine($"{n}. {kind.Count} times, {kind.FirstOn:yyyy-MM-dd} to {kind.LastOn:yyyy-MM-dd}. " +
                               (kind.Retried == 0 ? "Never retried." : $"Retried {kind.Retried} times; the retry succeeded {kind.FixedByRetry} times."));
            message.AppendLine($"   {kind.Masked}");
        }
        if (kinds.Count == 0) message.AppendLine("(none)");
        message.AppendLine("</failures>");
        return message.ToString();
    }

    static readonly Regex Url = new(@"[a-z][a-z0-9+.-]*://[^\s'""<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>An error's first line as the model may see it: masked, with addresses taken out too.</summary>
    public static string Mask(string firstLine) => Url.Replace(new Masking().Error(firstLine), "<url>");

    /// <summary>
    /// The model's answer as a policy that could be saved, or a validation error saying why not.
    /// Alerts are never taken from it: where a policy's alerts go is a person's choice.
    /// </summary>
    public static RetrySuggestion Parse(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        RawSuggestion? raw = null;
        if (start >= 0 && end > start)
        {
            try
            {
                raw = JObject.Parse(answer[start..(end + 1)]).ToObject<RawSuggestion>(JsonSerializer.Create(Settings));
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
            {
            }
        }
        if (raw?.Groups is not { Count: > 0 })
            throw new SWValidationException("AI_SUGGESTION", "The AI's answer wasn't a retry policy. Try again.");

        var groups = raw.Groups.Select((g, i) => new RetryGroup
        {
            Name = string.IsNullOrWhiteSpace(g.Name) ? $"Group {i + 1}" : g.Name.Trim(),
            Priority = g.Priority ?? i,
            AppliesTo = g.AppliesTo?.Where(t => t != XchangeResultType.Success).Distinct().ToList() ?? [],
            Matchers = g.Matchers?.Where(m => m is not null).ToList() ?? [],
            Action = g.Action ?? RetryAction.Allow,
            Budget = g.Action == RetryAction.Block ? null : g.Budget,
            Notes = g.Notes,
        }).ToList();

        try
        {
            SW.Bitween.Resources.RetryPolicies.RetryGroupValidation.EnsureCanFire(groups);
        }
        catch (SWValidationException e)
        {
            throw new SWValidationException("AI_SUGGESTION", $"The AI suggested a policy that can't be saved: {e.Message} Try again.");
        }

        return new RetrySuggestion(
            string.IsNullOrWhiteSpace(raw.Name) ? "Suggested retry policy" : raw.Name.Trim(),
            raw.Summary?.Trim() ?? "", groups);
    }

    /// <summary>What a policy does with one failure: how many times it retries it, by which group, or why it stops.</summary>
    public static async Task<string> Outcome(IReadOnlyList<RetryGroup>? groups, string content)
    {
        if (groups is not { Count: > 0 }) return "Not retried: no retry policy";
        var evaluator = new RetryPolicyEvaluator(new CustomRetryPolicy { Groups = groups.ToList() }, new InMemoryRetryGroupBudget());
        var retries = 0;
        string? by = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var decision = await evaluator.Evaluate(XchangeResultType.Error, content, attempt);
            if (!decision.ShouldRetry)
                return retries == 0 ? $"Not retried: {decision.Reason}" : $"Retried up to {retries} times by \"{by}\"";
            retries++;
            by = decision.MatchedGroupName;
        }
        return $"Retried {retries}+ times by \"{by}\"";
    }

    /// <summary>The wire's own settings: camelCase, enums as names, matchers and delays by their type.</summary>
    static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { new MatcherJsonConverter(), new DelayStrategyJsonConverter(), new Newtonsoft.Json.Converters.StringEnumConverter() },
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    // Loose on purpose: whatever the model leaves out gets a default or is caught by validation.
    sealed class RawSuggestion
    {
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public List<RawGroup>? Groups { get; set; }
    }

    sealed class RawGroup
    {
        public string? Name { get; set; }
        public int? Priority { get; set; }
        public List<XchangeResultType>? AppliesTo { get; set; }
        public List<Matcher>? Matchers { get; set; }
        public RetryAction? Action { get; set; }
        public RetryBudget? Budget { get; set; }
        public string? Notes { get; set; }
    }
}
