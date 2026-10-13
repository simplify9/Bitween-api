using System.Net;
using System.Text;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// A message as an email: the title for a subject, an HTML body, and the same again as plain text
/// for clients that will not show HTML.
/// </summary>
/// <remarks>
/// Inline styles and a table, because that is all most mail clients honour. Every value is encoded:
/// an error message is text from somewhere else, and markup in it must not become the email's markup.
/// </remarks>
internal static class EmailBody
{
    public static (string Subject, string Html, string Text) Render(ChannelMessage message)
    {
        // A subject is one line; a title with a line break in it would be cut there by most clients.
        var subject = message.Title.ReplaceLineEndings(" ");
        return (subject, Html(message), Text(message));
    }

    private static string Html(ChannelMessage message)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:'Segoe UI',Helvetica,Arial,sans-serif;font-size:14px;line-height:1.5;color:#1f2328;max-width:640px\">");
        html.Append($"<div style=\"border-left:4px solid {Color(message.Tone)};padding:2px 0 2px 12px;margin:0 0 16px\">");
        html.Append($"<div style=\"font-size:18px;font-weight:600\">{E(message.Title)}</div>");
        if (!string.IsNullOrWhiteSpace(message.Text))
            html.Append($"<div style=\"margin-top:4px;color:#59636e\">{E(message.Text)}</div>");
        html.Append("</div>");

        if (message.Facts.Count > 0)
        {
            html.Append("<table style=\"border-collapse:collapse;margin:0 0 16px\">");
            foreach (var fact in message.Facts)
                html.Append("<tr>")
                    .Append($"<td style=\"padding:2px 16px 2px 0;color:#59636e;white-space:nowrap;vertical-align:top\">{E(fact.Label)}</td>")
                    .Append($"<td style=\"padding:2px 0;word-break:break-word\">{E(fact.Value)}</td>")
                    .Append("</tr>");
            html.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(message.Detail))
            html.Append("<pre style=\"background:#f6f8fa;border-radius:6px;padding:12px;margin:0 0 16px;font-size:12px;white-space:pre-wrap;word-break:break-word\">")
                .Append(E(message.Detail))
                .Append("</pre>");

        if (!string.IsNullOrWhiteSpace(message.LinkUrl))
            html.Append($"<p style=\"margin:0 0 16px\"><a href=\"{E(message.LinkUrl)}\" style=\"display:inline-block;background:#1f2328;color:#ffffff;text-decoration:none;padding:8px 14px;border-radius:6px\">")
                .Append(E(message.LinkLabel ?? "Open in Bitween"))
                .Append("</a></p>");

        html.Append("<p style=\"margin:24px 0 0;color:#8c959f;font-size:12px\">Sent by Bitween.</p>");
        html.Append("</div>");
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
        return text.ToString();
    }

    private static string Color(ChannelMessageTone tone) => tone switch
    {
        ChannelMessageTone.Failure => "#cf222e",
        ChannelMessageTone.Warning => "#bf8700",
        ChannelMessageTone.Success => "#1a7f37",
        _ => "#0969da"
    };
}
