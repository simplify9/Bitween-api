#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// Documents and error text with every value taken out, so an AI provider sees how an exchange is
/// shaped and where it broke but none of what it carried.
/// </summary>
/// <remarks>
/// <para>
/// A document keeps its keys, nesting and the type of each value: <c>"name": "Lina"</c> becomes
/// <c>"name": "&lt;text&gt;"</c>. That is nearly always enough to explain a failure - a missing field,
/// a number where text was wanted, an empty list - and it is the only thing sent.
/// </para>
/// <para>
/// Error text is free text, so it is masked three ways: a document inside it (an HTTP adapter puts
/// the partner's response body there) is masked as a document; every value the exchange's own
/// documents hold is replaced wherever it appears; and quoted text, long numbers and email
/// addresses are replaced whatever they are. What survives is the error's wording and the code's
/// stack, which is where an explanation comes from.
/// </para>
/// </remarks>
public sealed class Masking
{
    public const string Text = "<text>";
    public const string Number = "<number>";
    public const string Secret = "<secret>";
    public const string Boolean = "<true|false>";

    /// <summary>A long list shows this many entries, then how many more there were.</summary>
    const int ListEntries = 3;

    /// <summary>How much of one masked document or error is sent.</summary>
    public const int MaxLength = 12_000;

    // Every value any masked document held, so error text naming one can have it taken out.
    readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A document with its values replaced by their types, or null when it can't be read as JSON, XML
    /// or CSV - in which case nothing of it is sent.
    /// </summary>
    /// <param name="contentType">The file's content type, when known; it picks the reader.</param>
    public string? Document(string text, string? contentType)
    {
        foreach (var format in FormatsFor(contentType))
        {
            ValueNode tree;
            try
            {
                tree = format.Read(text);
            }
            catch (DocumentFormatException)
            {
                continue;
            }

            try
            {
                return Cut(format.Write(Mask(tree)));
            }
            catch (DocumentFormatException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Error text with documents, values and anything that looks like data taken out.</summary>
    public string Error(string text)
    {
        // A document inside is masked as a document; the words around it as free text. Free-text
        // rules run on the words only, or they would take a masked document's keys as quoted text.
        var (before, document, after) = EmbeddedDocument(text);
        return Cut(Words(before) + document + Words(after));
    }

    string Words(string text)
    {
        // Longest first, so "Lina Haddad" goes before "Lina" can leave " Haddad" behind.
        foreach (var value in seen.Where(v => v.Length >= 3).OrderByDescending(v => v.Length))
            text = text.Replace(value, Text, StringComparison.OrdinalIgnoreCase);

        // A stack frame's file keeps its name: the folders above it describe the server, not the failure.
        text = SourcePath.Replace(text, " in ${file}:line");
        text = Email.Replace(text, "<email>");
        text = Quoted.Replace(text, m => $"{m.Groups["q"].Value}{Text}{m.Groups["q"].Value}");
        text = QueryString.Replace(text, "?<query>");
        // Credentials a partner's error can echo unquoted: "Bearer eyJ...", "api_key=...", or a bare token.
        text = Scheme.Replace(text, m => $"{m.Groups["scheme"].Value} {Secret}");
        text = Labelled.Replace(text, m => $"{m.Groups["label"].Value}{m.Groups["sep"].Value}{Secret}");
        text = TokenLike.Replace(text, Secret);
        return LongNumber.Replace(text, Number);
    }

    ValueNode Mask(ValueNode node)
    {
        switch (node)
        {
            case ScalarNode scalar:
                switch (scalar.Value)
                {
                    case null:
                        return scalar;
                    case string s:
                        if (s.Trim().Length > 0) seen.Add(s.Trim());
                        return ValueNode.Value(s.Length == 0 ? "" : Text);
                    case bool:
                        return ValueNode.Value(Boolean);
                    default:
                        seen.Add(Convert.ToString(scalar.Value, System.Globalization.CultureInfo.InvariantCulture)!);
                        return ValueNode.Value(Number);
                }

            case ObjectNode obj:
                var copy = ValueNode.Object();
                foreach (var (key, child) in obj.Children()) copy.Set(key, Mask(child));
                return copy;

            case ListNode list:
                var masked = ValueNode.List();
                foreach (var item in list.Items.Take(ListEntries)) masked.Add(Mask(item));
                // Still walked, so their values are known to be taken out of the error text.
                foreach (var item in list.Items.Skip(ListEntries)) Mask(item);
                if (list.Items.Count > ListEntries)
                    masked.Add(ValueNode.Value($"<{list.Items.Count - ListEntries} more entries like these>"));
                return masked;

            default:
                return node;
        }
    }

    /// <summary>The text before the first JSON or XML document inside it, that document masked, and the text after.</summary>
    (string Before, string Document, string After) EmbeddedDocument(string text)
    {
        foreach (var (open, close, format) in new (char, char, IDocumentFormat)[]
                 { ('{', '}', new JsonFormat()), ('[', ']', new JsonFormat()), ('<', '>', new XmlFormat()) })
        {
            var start = text.IndexOf(open);
            var end = text.LastIndexOf(close);
            if (start < 0 || end <= start) continue;

            var inner = text[start..(end + 1)];
            try
            {
                var masked = format.Write(Mask(format.Read(inner)));
                return (text[..start], masked, text[(end + 1)..]);
            }
            catch (DocumentFormatException)
            {
                // Not a document after all - a stack frame's [brackets], say.
            }
        }
        return (text, "", "");
    }

    static IEnumerable<IDocumentFormat> FormatsFor(string? contentType)
    {
        var type = contentType?.ToLowerInvariant() ?? "";
        if (type.Contains("json")) yield return new JsonFormat();
        else if (type.Contains("xml")) yield return new XmlFormat();
        // Read without a header: a header's names would be kept as keys, and a file without one
        // would send its first row as them. Every row is masked, a real header row included.
        else if (type.Contains("csv")) yield return new CsvFormat(new CsvOptions { HasHeader = false });
        else
        {
            // Unknown: the two formats that say so of themselves. CSV is never guessed - nearly any
            // text reads as CSV, and its values would then be the whole of it.
            yield return new JsonFormat();
            yield return new XmlFormat();
        }
    }

    static string Cut(string text) =>
        text.Length <= MaxLength ? text : text[..MaxLength] + $"\n<{text.Length - MaxLength} more characters not sent>";

    static readonly Regex SourcePath = new(@" in [^\s]*?[\\/](?<file>[^\\/\s]+):line", RegexOptions.Compiled);
    static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
    static readonly Regex Quoted = new(@"(?<q>['""])(?!<)[^'""\r\n]{1,200}\k<q>", RegexOptions.Compiled);
    static readonly Regex QueryString = new(@"\?[^\s'""<>]+", RegexOptions.Compiled);
    static readonly Regex LongNumber = new(@"(?<![\w.:])\d{4,}(?![\w])", RegexOptions.Compiled);
    static readonly Regex Scheme = new(@"\b(?<scheme>bearer|basic)\s+(?!<)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Labelled = new(
        @"(?<label>\b(?:password|passwd|pwd|secret|client[_-]?secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key|access[_-]?key|authorization)\b)(?<sep>\s*[:=]\s*)(?!<)(?!(?:bearer|basic)\b)[^\s,;&'""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 20 or more characters mixing letters and digits: keys, tokens, hashes, ids. Exception and
    // namespace names have no digits, and dots split them anyway.
    static readonly Regex TokenLike = new(@"(?<![\w+/=-])(?=[A-Za-z0-9_+/=-]*\d)(?=[A-Za-z0-9_+/=-]*[A-Za-z])[A-Za-z0-9_+/=-]{20,}(?![\w+/=-])", RegexOptions.Compiled);
}
