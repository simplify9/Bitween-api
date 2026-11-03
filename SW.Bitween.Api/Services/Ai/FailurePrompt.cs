#nullable enable
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.Services.Ai;

/// <summary>An exchange's failure in plain words: what happened, why, and what to change.</summary>
public sealed record FailureExplanation(string Summary, string Cause, string Fix);

/// <summary>What a model is told to explain a failed exchange, and how its answer is read.</summary>
public static class FailurePrompt
{
    public const string Instructions = """
        You explain why an exchange failed in Bitween, an integration platform, to the person running it.
        They know their partners and subscriptions; they may not be developers.

        How Bitween works: a partner or a receiver delivers a document; a subscription picks it up; its
        mapper turns the document into what the destination expects; its handler delivers that, often
        over HTTP, and may get a response back. A failure is either in mapping or in delivery. Failed
        exchanges can be retried by a retry policy, or by hand.

        When the exchange was retried, you also get its whole retry chain: every attempt in order, its
        outcome and its error's first line. Use it: an error that repeats unchanged points at a lasting
        problem - unless it is one a retry can outlast (a timeout, a 5xx, a rate limit), which repeating
        means an outage still going on. One that changed or passed points at a glitch or a fix that
        already happened. If a later attempt succeeded, say so first.

        Every value in the documents and the error has been masked before you see it: text is <text>,
        numbers are <number>, booleans are <true|false>, credentials and tokens are <secret>, and long lists show their first entries. Keys,
        structure and the error's wording are real. Never guess at a masked value; reason from structure,
        names, types and the error.

        Answer with exactly one JSON object and nothing else:
        { "summary": "...", "cause": "...", "fix": "..." }
        - summary: one sentence, what went wrong, in plain words.
        - cause: the most likely reason, two to four sentences, pointing at the field, setting or step
          involved. Say how sure you are when the evidence is thin.
        - fix: the change most likely to make the next attempt succeed, concrete and short - which setting,
          rule or field to change and to what, or who to ask (the partner, an administrator). If a retry
          alone would help (a timeout, a 5xx, a rate limit), say so.
        """;

    /// <summary>The model's answer as an explanation; a plain-text answer becomes the summary.</summary>
    public static FailureExplanation Parse(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var json = JObject.Parse(answer[start..(end + 1)]);
                var summary = Text(json["summary"]);
                if (!string.IsNullOrWhiteSpace(summary))
                    return new(summary.Trim(), (Text(json["cause"]) ?? "").Trim(), (Text(json["fix"]) ?? "").Trim());
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Read as prose below.
            }
        }
        return new(answer.Trim(), "", "");
    }

    /// <summary>A field as text, whatever the model made it: a fix often comes back as a list of steps.</summary>
    static string? Text(JToken? token) => token switch
    {
        null or { Type: JTokenType.Null } => null,
        JValue value => value.ToString(),
        JArray list => string.Join("\n", list.Select(i => i.Type == JTokenType.String ? (string?)i : i.ToString(Newtonsoft.Json.Formatting.None))),
        _ => token.ToString(Newtonsoft.Json.Formatting.None),
    };
}
