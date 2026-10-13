using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// A notification written for a person: what happened, the few facts that place it, the error if
/// there was one, and where to look. Each messaging channel lays it out its own way.
/// </summary>
/// <remarks>
/// Bitween writes it from whatever is being reported, so the Email, Teams and Slack adapters never
/// need to know what an exchange or a retry budget is, only how to show a message.
/// </remarks>
public class ChannelMessage
{
    public string Title { get; set; } = string.Empty;

    [JsonConverter(typeof(StringEnumConverter))]
    public ChannelMessageTone Tone { get; set; }

    /// <summary>One or two sentences under the title.</summary>
    public string? Text { get; set; }

    public List<ChannelMessageFact> Facts { get; set; } = [];

    /// <summary>Shown as it is in a monospace block: an error, typically, see <see cref="WithoutStackTrace"/>.</summary>
    public string? Detail { get; set; }

    public string? LinkUrl { get; set; }
    public string? LinkLabel { get; set; }

    /// <summary>
    /// Reads a message, refusing anything else: these adapters are picked through a notification
    /// channel, and a document handed to one by any other route has nothing to show.
    /// </summary>
    public static ChannelMessage Parse(string? json)
    {
        ChannelMessage? message = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(json)) message = JsonConvert.DeserializeObject<ChannelMessage>(json);
        }
        catch (JsonException)
        {
        }

        // A fact with nothing to say, such as the information type of an exchange since removed,
        // is left out rather than shown blank, or refused by Slack and Teams.
        message?.Facts?.RemoveAll(f => f is null || string.IsNullOrWhiteSpace(f.Label) || string.IsNullOrWhiteSpace(f.Value));
        if (message is { Facts: null }) message.Facts = [];

        return message is { Title.Length: > 0 }
            ? message
            : throw new InvalidOperationException(
                "This adapter only sends Bitween's notification messages. Use it through a notification channel.");
    }

    /// <summary>
    /// An exception as a person reads it: its message and its inner ones, without the stack frames
    /// that follow them. Those help a developer, who has the exchange the message links to; in an
    /// email or a chat they bury the one line that says what went wrong.
    /// </summary>
    public static string? WithoutStackTrace(string? exception)
    {
        if (string.IsNullOrWhiteSpace(exception)) return exception;
        var kept = exception.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("   at ", StringComparison.Ordinal) &&
                           !line.TrimStart().StartsWith("--- End of ", StringComparison.Ordinal))
            .ToList();
        var text = string.Join("\n", kept).TrimEnd();
        return text.Length > 0 ? text : exception;
    }

    /// <summary>Cuts <paramref name="text"/> to <paramref name="limit"/> characters, marking the cut.</summary>
    internal static string Shorten(string text, int limit) =>
        text.Length <= limit ? text : text[..(limit - 1)] + "…";
}

public enum ChannelMessageTone
{
    Info,
    Success,
    Warning,
    Failure
}

public record ChannelMessageFact(string Label, string Value);
