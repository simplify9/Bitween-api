#nullable enable
using System;
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

        Every value in the documents and the error has been masked before you see it: text is <text>,
        numbers are <number>, booleans are <true|false>, and long lists show their first entries. Keys,
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
                var summary = (string?)json["summary"];
                if (!string.IsNullOrWhiteSpace(summary))
                    return new(summary.Trim(), ((string?)json["cause"] ?? "").Trim(), ((string?)json["fix"] ?? "").Trim());
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Read as prose below.
            }
        }
        return new(answer.Trim(), "", "");
    }
}
