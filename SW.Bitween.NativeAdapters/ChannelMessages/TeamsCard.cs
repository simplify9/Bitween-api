using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// A message as the body a Teams Workflows webhook posts: one Adaptive Card wrapped in a message.
/// </summary>
/// <remarks>
/// Version 1.4, the newest every Teams client renders: desktop, web and mobile alike.
/// </remarks>
internal static class TeamsCard
{
    /// <summary>Teams cuts a card off long before this; past it, the link to the full error is the better read.</summary>
    private const int DetailLimit = 2000;

    public static string Render(ChannelMessage message)
    {
        var body = new JArray
        {
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = message.Title,
                ["weight"] = "Bolder",
                ["size"] = "Medium",
                ["color"] = Color(message.Tone),
                ["wrap"] = true
            }
        };

        if (!string.IsNullOrWhiteSpace(message.Text))
            body.Add(new JObject { ["type"] = "TextBlock", ["text"] = message.Text, ["wrap"] = true });

        if (message.Facts.Count > 0)
            body.Add(new JObject
            {
                ["type"] = "FactSet",
                ["facts"] = new JArray(message.Facts.Select(f => new JObject
                {
                    ["title"] = f.Label,
                    ["value"] = f.Value
                }))
            });

        if (!string.IsNullOrWhiteSpace(message.Detail))
            body.Add(new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = ChannelMessage.Shorten(message.Detail, DetailLimit),
                ["fontType"] = "Monospace",
                ["size"] = "Small",
                ["isSubtle"] = true,
                ["wrap"] = true
            });

        var card = new JObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
            // Without it Teams squeezes the card into a narrow column, and an error wraps every few words.
            ["msteams"] = new JObject { ["width"] = "Full" }
        };

        if (!string.IsNullOrWhiteSpace(message.LinkUrl))
            card["actions"] = new JArray(new JObject
            {
                ["type"] = "Action.OpenUrl",
                ["title"] = message.LinkLabel ?? "Open in Bitween",
                ["url"] = message.LinkUrl
            });

        return new JObject
        {
            ["type"] = "message",
            ["attachments"] = new JArray(new JObject
            {
                ["contentType"] = "application/vnd.microsoft.card.adaptive",
                ["contentUrl"] = null,
                ["content"] = card
            })
        }.ToString(Formatting.None);
    }

    private static string Color(ChannelMessageTone tone) => tone switch
    {
        ChannelMessageTone.Failure => "Attention",
        ChannelMessageTone.Warning => "Warning",
        ChannelMessageTone.Success => "Good",
        _ => "Default"
    };
}
