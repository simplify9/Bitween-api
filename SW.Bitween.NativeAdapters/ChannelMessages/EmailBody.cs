using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// A message as an email: the title for a subject, an HTML body, and the same again as plain text
/// for clients that will not show HTML.
/// </summary>
/// <remarks>
/// <para>
/// Laid out with tables and inline styles, because that is all Outlook and most webmail honour:
/// no stylesheet, no flexbox, no SVG. The logo is a PNG sent inside the email and referred to by
/// <see cref="LogoContentId"/>, so it shows wherever the email is read, including outside the
/// network the instance runs in and in clients that block remote images.
/// </para>
/// <para>
/// Every value is encoded: an error message is text from somewhere else, and markup in it must not
/// become the email's markup. The accent is the instance's brand colour, taken only when it is a
/// plain hex colour, since it is written into a style attribute.
/// </para>
/// </remarks>
internal static class EmailBody
{
    /// <summary>The <c>cid:</c> the HTML shows the logo by; the sender attaches it under this id.</summary>
    public const string LogoContentId = "bitween-logo";

    public const string DefaultAccent = "#e3311d";

    const string Ink = "#201b1a";
    const string Body = "#4d4341";
    const string Muted = "#7f726e";
    const string Line = "#ece7e4";
    const string Wash = "#f6f3f1";
    const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif";

    static readonly Regex HexColour = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    /// <summary>The logo the HTML refers to, as PNG bytes.</summary>
    public static byte[] Logo()
    {
        using var stream = typeof(EmailBody).Assembly.GetManifestResourceStream(
            "SW.Bitween.NativeAdapters.ChannelMessages.bitween-logo.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <param name="accent">The brand colour for the top bar and the button; Bitween's red when unset or not a hex colour.</param>
    public static (string Subject, string Html, string Text) Render(ChannelMessage message, string? accent = null)
    {
        // A subject is one line; a title with a line break in it would be cut there by most clients.
        var subject = message.Title.ReplaceLineEndings(" ");
        var colour = accent is not null && HexColour.IsMatch(accent.Trim()) ? accent.Trim() : DefaultAccent;
        return (subject, Html(message, colour), Text(message));
    }

    private static string Html(ChannelMessage message, string accent)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            // Laid out for a light background; without these, some clients invert it unevenly.
            .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
            .Append($"<title>{E(message.Title)}</title></head>")
            .Append($"<body style=\"margin:0;padding:0;background:{Wash};\">");

        // The first line of text, shown beside the subject in an inbox.
        var preview = message.Text ?? message.Title;
        html.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{E(preview)}</div>");

        html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"{Wash}\" style=\"background:{Wash};\">")
            .Append("<tr><td align=\"center\" style=\"padding:32px 12px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:560px;\">");

        // The card: the brand colour across the top, then the logo.
        html.Append($"<tr><td bgcolor=\"#ffffff\" style=\"background:#ffffff;border:1px solid {Line};border-top:4px solid {accent};border-radius:12px;padding:28px 32px 32px;font-family:{Font};\">")
            .Append($"<img src=\"cid:{LogoContentId}\" width=\"122\" height=\"22\" alt=\"Bitween\" style=\"display:block;width:122px;height:22px;border:0;outline:none;margin:0 0 28px;\">");

        // The title, marked with what kind of message it is - except plain information, which in the
        // brand's red would read as an error.
        var title = $"<div style=\"font-family:{Font};font-size:20px;line-height:1.3;font-weight:600;color:{Ink};\">{E(message.Title)}</div>";
        if (Tone(message.Tone) is { } tone)
            html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td style=\"border-left:4px solid {tone};padding:2px 0 2px 14px;\">")
                .Append(title)
                .Append("</td></tr></table>");
        else
            html.Append(title);

        if (!string.IsNullOrWhiteSpace(message.Text))
            html.Append($"<p style=\"margin:16px 0 0;font-family:{Font};font-size:15px;line-height:1.6;color:{Body};\">{E(message.Text)}</p>");

        if (message.Facts.Count > 0)
        {
            html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"{Wash}\" style=\"margin:20px 0 0;background:{Wash};border-radius:8px;\">");
            foreach (var fact in message.Facts)
                html.Append("<tr>")
                    .Append($"<td valign=\"top\" style=\"padding:8px 16px;font-family:{Font};font-size:13px;line-height:1.5;color:{Muted};white-space:nowrap;width:1%;\">{E(fact.Label)}</td>")
                    .Append($"<td valign=\"top\" style=\"padding:8px 16px 8px 0;font-family:{Font};font-size:13px;line-height:1.5;color:{Ink};word-break:break-word;\">{E(fact.Value)}</td>")
                    .Append("</tr>");
            html.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(message.Detail))
            html.Append($"<pre style=\"margin:20px 0 0;background:{Wash};border-radius:8px;padding:14px 16px;font-family:'SF Mono',Menlo,Consolas,monospace;font-size:12px;line-height:1.5;color:{Ink};white-space:pre-wrap;word-break:break-word;\">")
                .Append(E(message.Detail))
                .Append("</pre>");

        if (!string.IsNullOrWhiteSpace(message.LinkUrl))
        {
            var url = E(message.LinkUrl);
            // A button made of a table cell, which Outlook draws as one.
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:28px 0 0;\"><tr>")
                .Append($"<td bgcolor=\"{accent}\" style=\"background:{accent};border-radius:8px;\">")
                .Append($"<a href=\"{url}\" style=\"display:inline-block;padding:12px 22px;font-family:{Font};font-size:15px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:8px;\">")
                .Append(E(message.LinkLabel ?? "Open in Bitween"))
                .Append("</a></td></tr></table>")
                // For a client that shows no button, and for copying into another browser.
                .Append($"<p style=\"margin:16px 0 0;font-family:{Font};font-size:12px;line-height:1.5;color:{Muted};\">Or open this link: ")
                .Append($"<a href=\"{url}\" style=\"color:{Muted};text-decoration:underline;word-break:break-all;\">{url}</a></p>");
        }

        html.Append("</td></tr>");

        html.Append($"<tr><td align=\"center\" style=\"padding:20px 12px 0;font-family:{Font};font-size:12px;line-height:1.5;color:{Muted};\">Sent by Bitween</td></tr>")
            .Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static string Text(ChannelMessage message)
    {
        var text = new StringBuilder(message.Title).AppendLine();
        if (!string.IsNullOrWhiteSpace(message.Text)) text.AppendLine().AppendLine(message.Text);
        if (message.Facts.Count > 0)
        {
            text.AppendLine();
            foreach (var fact in message.Facts) text.AppendLine($"{fact.Label}: {fact.Value}");
        }
        if (!string.IsNullOrWhiteSpace(message.Detail)) text.AppendLine().AppendLine(message.Detail);
        if (!string.IsNullOrWhiteSpace(message.LinkUrl))
            text.AppendLine().AppendLine($"{message.LinkLabel ?? "Open in Bitween"}: {message.LinkUrl}");
        text.AppendLine().AppendLine("Sent by Bitween");
        return text.ToString();
    }

    /// <summary>What kind of message it is, beside its title; null for plain information.</summary>
    private static string? Tone(ChannelMessageTone tone) => tone switch
    {
        ChannelMessageTone.Failure => "#cf222e",
        ChannelMessageTone.Warning => "#bf8700",
        ChannelMessageTone.Success => "#1a7f37",
        _ => null
    };
}
