using System;
using System.Collections.Generic;
using System.Globalization;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.Bitween.NativeAdapters.EmailMessageHandler;
using SW.Bitween.NativeAdapters.SlackMessageHandler;
using SW.Bitween.NativeAdapters.TeamsMessageHandler;

namespace SW.Bitween;

/// <summary>
/// Writes the message an Email, Teams or Slack channel sends, from whatever is being reported.
/// </summary>
/// <remarks>
/// Every other adapter is handed the notification itself, as it always was, so a custom adapter or
/// an SMTP template written against its fields keeps working.
/// </remarks>
public static class ChannelMessages
{
    /// <summary>The adapters that are sent a <see cref="ChannelMessage"/> rather than the notification.</summary>
    public static readonly IReadOnlySet<string> AdapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        nameof(NativeEmailMessageHandler),
        nameof(NativeTeamsMessageHandler),
        nameof(NativeSlackMessageHandler)
    };

    /// <param name="linkBase">Where Bitween's pages are, for the link; null leaves the link out.</param>
    public static ChannelMessage For(object payload, string linkBase) => payload switch
    {
        ChannelMessage message => message,
        XchangeResultNotification result => ForResult(result, linkBase),
        _ => throw new ArgumentException($"There is no message for a {payload?.GetType().Name}.", nameof(payload))
    };

    /// <summary>A made-up message for the channel's test button, so whoever set it up can see one arrive.</summary>
    public static ChannelMessage Test(string channelName) => new()
    {
        Title = "Test message from Bitween",
        Tone = ChannelMessageTone.Info,
        Text = $"If you can read this, the notification channel \"{channelName}\" works.",
        Facts = [new("Sent", When(DateTime.UtcNow))]
    };

    private static ChannelMessage ForResult(XchangeResultNotification result, string linkBase)
    {
        var (title, tone) = !result.Success
            ? ($"{result.SubscriptionName} failed", ChannelMessageTone.Failure)
            : result.ResponseBad
                ? ($"{result.SubscriptionName} finished with a bad result", ChannelMessageTone.Warning)
                : ($"{result.SubscriptionName} succeeded", ChannelMessageTone.Success);

        var facts = new List<ChannelMessageFact>
        {
            new("Subscription", result.SubscriptionName),
            new("Information type", result.DocumentName),
            new("Exchange", result.Id)
        };
        if (!string.IsNullOrWhiteSpace(result.CorrelationId) && result.CorrelationId != result.Id)
            facts.Add(new("Correlation id", result.CorrelationId));
        facts.Add(new("Started", When(result.StartedOn)));
        facts.Add(new("Finished", When(result.FinishedOn)));

        return new ChannelMessage
        {
            Title = title,
            Tone = tone,
            // The quiet window held these back; saying so is what makes one message read as an outage.
            Text = result.SuppressedSinceLast switch
            {
                0 => null,
                1 => "1 more like this since the last message was not sent separately.",
                var n => $"{n} more like this since the last message were not sent separately."
            },
            Facts = facts,
            Detail = result.Exception,
            LinkUrl = ExchangeLink(linkBase, result.Id),
            LinkLabel = "Open the exchange"
        };
    }

    private static string ExchangeLink(string linkBase, string xchangeId) =>
        linkBase == null || string.IsNullOrEmpty(xchangeId)
            ? null
            : $"{linkBase}/exchanges?ids={Uri.EscapeDataString(xchangeId)}";

    /// <summary>Bitween keeps every time in UTC, and says so rather than guess the reader's zone.</summary>
    private static string When(DateTime utc) =>
        utc.ToString("d MMM yyyy, HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
