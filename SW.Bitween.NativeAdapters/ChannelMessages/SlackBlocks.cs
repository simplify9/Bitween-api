using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// A message as the body a Slack incoming webhook posts: Block Kit blocks, plus plain text for the
/// notification banner and for clients that cannot show blocks.
/// </summary>
/// <remarks>
/// Slack rejects the whole post when any block is over its limit, so every piece is cut to fit
/// rather than trusted to: 150 characters for a header, 2,000 for a field, 3,000 for a section.
/// </remarks>
internal static class SlackBlocks
{
    private const int HeaderLimit = 150;
    private const int FieldLimit = 2000;
    private const int SectionLimit = 3000;

    /// <summary>Slack's ceiling on fields in one section.</summary>
    private const int FieldsPerSection = 10;

    public static string Render(ChannelMessage message)
    {
        var blocks = new JArray
        {
            new JObject
            {
                ["type"] = "header",
                ["text"] = new JObject
                {
                    ["type"] = "plain_text",
                    ["text"] = ChannelMessage.Shorten($"{Emoji(message.Tone)} {message.Title}", HeaderLimit),
                    ["emoji"] = true
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(message.Text))
            blocks.Add(Section(ChannelMessage.Shorten(Escape(message.Text), SectionLimit)));

        foreach (var chunk in message.Facts.Chunk(FieldsPerSection))
            blocks.Add(new JObject
            {
                ["type"] = "section",
                ["fields"] = new JArray(chunk.Select(f => new JObject
                {
                    ["type"] = "mrkdwn",
                    ["text"] = ChannelMessage.Shorten($"*{Escape(f.Label)}*\n{Escape(f.Value)}", FieldLimit)
                }))
            });

        if (!string.IsNullOrWhiteSpace(message.Detail))
        {
            // Room for the fences either side; a fence inside the text would end the block early.
            var detail = Escape(message.Detail).Replace("```", "'''");
            blocks.Add(Section($"```{ChannelMessage.Shorten(detail, SectionLimit - 6)}```"));
        }

        if (!string.IsNullOrWhiteSpace(message.LinkUrl))
            blocks.Add(new JObject
            {
                ["type"] = "actions",
                ["elements"] = new JArray(new JObject
                {
                    ["type"] = "button",
                    ["text"] = new JObject { ["type"] = "plain_text", ["text"] = message.LinkLabel ?? "Open in Bitween" },
                    ["url"] = message.LinkUrl
                })
            });

        return new JObject
        {
            ["text"] = message.Title,
            ["blocks"] = blocks
        }.ToString(Formatting.None);
    }

    private static JObject Section(string mrkdwn) => new()
    {
        ["type"] = "section",
        ["text"] = new JObject { ["type"] = "mrkdwn", ["text"] = mrkdwn }
    };

    private static string Emoji(ChannelMessageTone tone) => tone switch
    {
        ChannelMessageTone.Failure => ":x:",
        ChannelMessageTone.Warning => ":warning:",
        ChannelMessageTone.Success => ":white_check_mark:",
        _ => ":information_source:"
    };

    /// <summary>The three characters Slack's markup reserves, as Slack asks for them to be escaped.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
