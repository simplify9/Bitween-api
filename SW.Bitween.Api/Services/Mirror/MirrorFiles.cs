using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SW.Bitween.Services.Mirror;

/// <summary>One of prod's files as staging keeps it: faked in its own format, or not kept, and why.</summary>
/// <param name="Content">The faked file; null when it isn't kept.</param>
/// <param name="Format">json, xml, html, csv, xlsx or text when it is kept; what it is when it isn't: pdf, zip, edi, binary...</param>
/// <param name="NotCopied">Why it isn't kept, in words for the page; null when it is.</param>
public record MirrorFakedFile(byte[] Content, string Name, string Format, string NotCopied);

/// <summary>
/// Fakes one of prod's files in its own format, so staging reads it the way prod read the real one:
/// JSON keeps its keys, XML its elements, attribute names and namespaces, CSV its delimiter, quoting and
/// line endings, an Excel workbook its sheets, styles and formulas. Only values change, the way
/// <see cref="MirrorFaker"/> changes any value. A file that can't be faked safely - a PDF, an archive, a
/// picture - isn't kept at all, so nothing of prod's is ever stored as it is.
/// </summary>
public static class MirrorFiles
{
    /// <summary>Larger files aren't kept: a sample needs the shape, not the volume.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    public static MirrorFakedFile Fake(MirrorFaker faker, byte[] content, string name)
    {
        var fakedName = faker.FileName(name);
        if (content.Length > MaxBytes) return TooLarge(fakedName);
        try
        {
            return Detect(content) switch
            {
                "xlsx" => Excel(faker, content, fakedName),
                "docx" => Refused(fakedName, "docx", "Word and PowerPoint files aren't faked yet."),
                "zip" => Refused(fakedName, "zip", "Archives aren't faked: what they hold could be anything."),
                "pdf" => Refused(fakedName, "pdf", "PDFs can't be faked: their text is drawn on the page, not kept as values."),
                "binary" => Refused(fakedName, "binary", "It isn't text, so it can't be faked."),
                _ => Text(faker, content, fakedName),
            };
        }
        // Said in general words: a parser's own message can quote the file.
        catch (Exception ex) when (ex is XmlException or JsonException or InvalidDataException or DecoderFallbackException or FormatException)
        {
            return Refused(fakedName, "unreadable", "It couldn't be read in its own format, so it wasn't kept.");
        }
    }

    public static MirrorFakedFile TooLarge(string name) =>
        Refused(name, "large", $"It is larger than {MaxBytes / 1024 / 1024} MB, and a sample needs the shape, not the size.");

    static MirrorFakedFile Kept(byte[] content, string name, string format) => new(content, name, format, null);

    static MirrorFakedFile Refused(string name, string format, string why) => new(null, name, format, why);

    // ------------------------------------------------------------------ what it is

    /// <summary>What a file is, read the way <see cref="Fake"/> reads it: json, xml, html, csv, text, xlsx, docx, zip, pdf, edi or binary.</summary>
    internal static string FormatOf(byte[] content)
    {
        var detected = Detect(content);
        if (detected != "text") return detected;
        var text = TextOf(content);
        var start = text.TrimStart();
        if (start.StartsWith('<'))
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                while (reader.Read()) { }
                return "xml";
            }
            catch (XmlException) { return "html"; }
        }
        if (start.StartsWith('{') || start.StartsWith('['))
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                return "json";
            }
            catch (JsonException) { }
        }
        if (start.StartsWith("UNA", StringComparison.Ordinal) || start.StartsWith("UNB", StringComparison.Ordinal) ||
            start.StartsWith("ISA", StringComparison.Ordinal))
            return "edi";
        return Csv.Delimiter(text) != null ? "csv" : "text";
    }

    /// <summary>A text file's text, in the encoding it was written in.</summary>
    internal static string TextOf(byte[] content) => Decode(content).Text;

    static string Detect(byte[] content)
    {
        var head = content.AsSpan(0, Math.Min(content.Length, 8192));
        if (head.StartsWith("PK\u0003\u0004"u8)) return Zip(content);
        if (head.StartsWith("%PDF-"u8)) return "pdf";
        if (head.StartsWith(Utf16Le) || head.StartsWith(Utf16Be)) return "text";
        return head.IndexOf((byte)0) >= 0 ? "binary" : "text";
    }

    static string Zip(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            if (names.Contains("[Content_Types].xml"))
            {
                if (names.Any(n => n.StartsWith("xl/", StringComparison.Ordinal))) return "xlsx";
                if (names.Any(n => n.StartsWith("word/", StringComparison.Ordinal) || n.StartsWith("ppt/", StringComparison.Ordinal))) return "docx";
            }
        }
        catch (InvalidDataException) { }
        return "zip";
    }

    // ------------------------------------------------------------------ text

    static MirrorFakedFile Text(MirrorFaker faker, byte[] content, string name)
    {
        var (text, encoding) = Decode(content);
        var start = text.TrimStart();
        if (start.StartsWith('<')) return Xml(faker, content, text, encoding, name);
        if (start.StartsWith('{') || start.StartsWith('['))
            if (faker.FakeJson(text) is { } json)
                return Kept(Encode(json, encoding), name, "json");
        if (start.StartsWith("UNA", StringComparison.Ordinal) || start.StartsWith("UNB", StringComparison.Ordinal) ||
            start.StartsWith("ISA", StringComparison.Ordinal))
            return Refused(name, "edi", "EDI files aren't faked yet: their codes would change along with their values.");
        if (Csv.Delimiter(text) is { } delimiter) return Kept(Encode(Csv.Fake(faker, text, delimiter), encoding), name, "csv");
        return Kept(Encode(faker.Fake(text), encoding), name, "text");
    }

    static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    static readonly byte[] Utf16Le = [0xFF, 0xFE];
    static readonly byte[] Utf16Be = [0xFE, 0xFF];
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Text as it was written: UTF-8 or UTF-16 by its byte order mark, UTF-8 when it reads as UTF-8, or
    /// else one byte to a character, which puts every byte back where it was. Faked letters are ASCII,
    /// so whatever the encoding, they fit it.
    /// </summary>
    static (string Text, Encoding Encoding) Decode(byte[] content)
    {
        var span = content.AsSpan();
        if (span.StartsWith(Utf8Bom)) return (StrictUtf8.GetString(content, 3, content.Length - 3), new UTF8Encoding(true));
        if (span.StartsWith(Utf16Le)) return (Encoding.Unicode.GetString(content, 2, content.Length - 2), new UnicodeEncoding(false, true));
        if (span.StartsWith(Utf16Be)) return (Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2), new UnicodeEncoding(true, true));
        try { return (StrictUtf8.GetString(content), new UTF8Encoding(false)); }
        catch (DecoderFallbackException) { return (Encoding.Latin1.GetString(content), Encoding.Latin1); }
    }

    static byte[] Encode(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    // ------------------------------------------------------------------ XML

    static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    static MirrorFakedFile Xml(MirrorFaker faker, byte[] content, string text, Encoding encoding, string name)
    {
        XDocument doc;
        try
        {
            // No DTDs and nothing fetched: a file of prod's never reaches out on staging's behalf.
            using var reader = XmlReader.Create(new MemoryStream(content),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // HTML, or XML that isn't well-formed: its tags kept, the text between them faked.
            return Kept(Encode(Markup(faker, text), encoding), name, "html");
        }

        FakeXml(faker, doc);
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
               {
                   Encoding = EncodingOf(doc.Declaration?.Encoding, content),
                   OmitXmlDeclaration = doc.Declaration == null,
                   NewLineHandling = NewLineHandling.None,
               }))
            doc.Save(writer);
        return Kept(output.ToArray(), name, "xml");
    }

    static Encoding EncodingOf(string declared, byte[] content)
    {
        var span = content.AsSpan();
        if (span.StartsWith(Utf16Le)) return new UnicodeEncoding(false, true);
        if (span.StartsWith(Utf16Be)) return new UnicodeEncoding(true, true);
        var bom = span.StartsWith(Utf8Bom);
        if (string.IsNullOrEmpty(declared) || declared.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) return new UTF8Encoding(bom);
        try { return Encoding.GetEncoding(declared); }
        catch (ArgumentException) { return new UTF8Encoding(bom); }
    }

    /// <summary>Every value faked - text, CDATA, comments, attributes - and every name kept: elements, attributes, namespaces, xsi:type.</summary>
    static void FakeXml(MirrorFaker faker, XContainer root)
    {
        foreach (var node in root.DescendantNodes().ToList())
        {
            switch (node)
            {
                case XCData cdata:
                    cdata.Value = Inner(faker, cdata.Value);
                    break;
                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    text.Value = Inner(faker, text.Value);
                    break;
                case XComment comment:
                    comment.Value = faker.Fake(comment.Value);
                    break;
                case XElement element:
                    foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.Namespace != Xsi))
                        attribute.Value = faker.Fake(attribute.Value);
                    break;
            }
        }
    }

    /// <summary>A value that is a document of its own, such as XML escaped into an element, faked as one; any other as a value.</summary>
    static string Inner(MirrorFaker faker, string value)
    {
        if (value.TrimStart().StartsWith('<'))
        {
            try
            {
                var inner = XDocument.Parse(value, LoadOptions.PreserveWhitespace);
                FakeXml(faker, inner);
                return (inner.Declaration?.ToString() ?? "") + inner.ToString(SaveOptions.DisableFormatting);
            }
            catch (XmlException) { }
        }
        return faker.Fake(value);
    }

    static readonly Regex Tag = new(@"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<[^<>]+>", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex AttributeValue = new(@"(?<=\s[\w:.\-]+\s*=\s*)(""[^""]*""|'[^']*')", RegexOptions.Compiled);
    static readonly Regex Entity = new(@"&(?:#[0-9]+|#x[0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);", RegexOptions.Compiled);

    /// <summary>Markup that isn't well-formed XML: tags kept with their attributes' values faked, comments, CDATA and the text between tags faked, entities kept.</summary>
    static string Markup(MirrorFaker faker, string text)
    {
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (Match tag in Tag.Matches(text))
        {
            result.Append(Between(faker, text[at..tag.Index]));
            result.Append(tag.Value.StartsWith("<!--", StringComparison.Ordinal) ? "<!--" + faker.Fake(tag.Value[4..^3]) + "-->"
                : tag.Value.StartsWith("<![CDATA[", StringComparison.Ordinal) ? "<![CDATA[" + faker.Fake(tag.Value[9..^3]) + "]]>"
                : AttributeValue.Replace(tag.Value, v => v.Value[0] + faker.Fake(v.Value[1..^1]) + v.Value[^1]));
            at = tag.Index + tag.Length;
        }
        result.Append(Between(faker, text[at..]));
        return result.ToString();
    }

    static string Between(MirrorFaker faker, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (Match entity in Entity.Matches(text))
        {
            result.Append(faker.Fake(text[at..entity.Index])).Append(entity.Value);
            at = entity.Index + entity.Length;
        }
        return result.Append(faker.Fake(text[at..])).ToString();
    }

    // ------------------------------------------------------------------ Excel

    /// <summary>Parts of a workbook that hold what can't be faked, with what to call them.</summary>
    static readonly (string Prefix, string What)[] Unkept =
    [
        ("xl/media/", "pictures"), ("xl/drawings/", "drawings"), ("xl/charts/", "charts"), ("xl/embeddings/", "embedded files"),
        ("xl/pivotCache/", "pivot tables"), ("xl/externalLinks/", "links to other workbooks"), ("customXml/", "custom data"),
    ];

    static MirrorFakedFile Excel(MirrorFaker faker, byte[] content, string name)
    {
        using var input = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        // A workbook is a zip: a small one can unpack to far more than any sample needs.
        if (input.Entries.Sum(e => e.Length) > 20L * MaxBytes)
            return Refused(name, "xlsx", $"It is larger than {20 * MaxBytes / 1024 / 1024} MB once unpacked, and a sample needs the shape, not the size.");
        var unkept = Unkept.Where(u => input.Entries.Any(e => e.FullName.StartsWith(u.Prefix, StringComparison.Ordinal))).Select(u => u.What).ToList();
        if (unkept.Count > 0) return Refused(name, "xlsx", $"It holds {string.Join(" and ", unkept)}, which can't be faked.");

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                var copy = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                using var source = entry.Open();
                using var sink = copy.Open();
                var part = entry.FullName;
                if (part == "xl/sharedStrings.xml" || part.StartsWith("xl/comments", StringComparison.Ordinal) ||
                    part.StartsWith("xl/threadedComments/", StringComparison.Ordinal) || part.StartsWith("docProps/", StringComparison.Ordinal))
                    Save(Texts(faker, Load(source)), sink);
                else if (part.StartsWith("xl/worksheets/", StringComparison.Ordinal) && part.EndsWith(".xml", StringComparison.Ordinal))
                    Save(Sheet(faker, Load(source)), sink);
                else if (part.EndsWith(".rels", StringComparison.Ordinal))
                    Save(Links(faker, Load(source)), sink);
                else
                    source.CopyTo(sink);
            }
        }
        return Kept(output.ToArray(), name, "xlsx");
    }

    static XDocument Load(Stream part)
    {
        using var reader = XmlReader.Create(part, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    static void Save(XDocument doc, Stream part)
    {
        using var writer = XmlWriter.Create(part, new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.None });
        doc.Save(writer);
    }

    /// <summary>Every element's own text faked: shared strings, comments, the workbook's author.</summary>
    static XDocument Texts(MirrorFaker faker, XDocument doc)
    {
        foreach (var element in doc.Descendants().Where(e => !e.HasElements && !string.IsNullOrEmpty(e.Value)))
            element.Value = faker.Fake(element.Value);
        return doc;
    }

    /// <summary>
    /// A sheet's cells: numbers keep their form, text written into the cell is faked, a shared string is
    /// faked where the strings are, and formulas, booleans and errors stay.
    /// </summary>
    static XDocument Sheet(MirrorFaker faker, XDocument doc)
    {
        foreach (var cell in doc.Descendants().Where(e => e.Name.LocalName == "c"))
        {
            var type = (string)cell.Attribute("t");
            var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
            switch (type)
            {
                case null or "n" when value != null:
                    value.Value = faker.Number(value.Value);
                    break;
                case "str" or "d" when value != null:
                    value.Value = faker.Fake(value.Value);
                    break;
                case "inlineStr":
                    foreach (var t in cell.Descendants().Where(e => e.Name.LocalName == "t"))
                        t.Value = faker.Fake(t.Value);
                    break;
            }
        }
        foreach (var link in doc.Descendants().Where(e => e.Name.LocalName == "hyperlink"))
        foreach (var attribute in new[] { "display", "tooltip" }.Select(a => link.Attribute(a)).Where(a => a != null))
            attribute.Value = faker.Fake(attribute.Value);
        return doc;
    }

    /// <summary>Links out of the workbook made mock ones, as any address is.</summary>
    static XDocument Links(MirrorFaker faker, XDocument doc)
    {
        foreach (var relation in doc.Descendants().Where(e => e.Name.LocalName == "Relationship" && (string)e.Attribute("TargetMode") == "External"))
            if (relation.Attribute("Target") is { } target)
                target.Value = faker.Addresses(target.Value);
        return doc;
    }

    // ------------------------------------------------------------------ CSV

    /// <summary>
    /// Delimited text read as written - RFC 4180 quoting, each field quoted or not as it was, each line's
    /// own ending - so it is written back the same with only its values faked.
    /// </summary>
    internal static class Csv
    {
        static readonly char[] Candidates = [',', ';', '\t', '|'];

        record struct Field(string Value, bool Quoted);

        record Row(List<Field> Fields, string Ending);

        /// <summary>
        /// The delimiter that splits every one of the first lines into the same number of fields, two or
        /// more, over at least two lines; the one giving the most fields when several do. Null for prose.
        /// </summary>
        public static char? Delimiter(string text)
        {
            char? best = null;
            var most = 1;
            foreach (var candidate in Candidates)
            {
                var rows = Parse(text, candidate, limit: 20).Where(r => r.Fields.Count > 1 || r.Fields[0].Value.Length > 0).ToList();
                if (rows.Count < 2) continue;
                var fields = rows[0].Fields.Count;
                if (fields > most && rows.All(r => r.Fields.Count == fields))
                {
                    best = candidate;
                    most = fields;
                }
            }
            return best;
        }

        /// <summary>Each row's values, as read.</summary>
        public static List<List<string>> Rows(string text, char delimiter) =>
            Parse(text, delimiter).Where(r => r.Fields.Count > 1 || r.Fields[0].Value.Length > 0)
                .Select(r => r.Fields.Select(f => f.Value).ToList()).ToList();

        public static string Fake(MirrorFaker faker, string text, char delimiter)
        {
            var result = new StringBuilder(text.Length);
            foreach (var row in Parse(text, delimiter))
            {
                for (var i = 0; i < row.Fields.Count; i++)
                {
                    if (i > 0) result.Append(delimiter);
                    var field = row.Fields[i];
                    var value = faker.Fake(field.Value);
                    result.Append(field.Quoted ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
                }
                result.Append(row.Ending);
            }
            return result.ToString();
        }

        static List<Row> Parse(string text, char delimiter, int limit = int.MaxValue)
        {
            var rows = new List<Row>();
            var fields = new List<Field>();
            var value = new StringBuilder();
            bool quoted = false, inQuotes = false;
            for (var i = 0; i < text.Length && rows.Count < limit; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c != '"') value.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else inQuotes = false;
                    continue;
                }
                if (c == '"' && value.Length == 0 && !quoted)
                {
                    inQuotes = quoted = true;
                    continue;
                }
                if (c == delimiter)
                {
                    fields.Add(new Field(value.ToString(), quoted));
                    value.Clear();
                    quoted = false;
                    continue;
                }
                if (c is '\r' or '\n')
                {
                    var ending = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : c.ToString();
                    i += ending.Length - 1;
                    fields.Add(new Field(value.ToString(), quoted));
                    rows.Add(new Row(fields, ending));
                    fields = [];
                    value.Clear();
                    quoted = false;
                    continue;
                }
                value.Append(c);
            }
            if (rows.Count < limit && (value.Length > 0 || quoted || fields.Count > 0))
            {
                fields.Add(new Field(value.ToString(), quoted));
                rows.Add(new Row(fields, ""));
            }
            return rows;
        }
    }
}
