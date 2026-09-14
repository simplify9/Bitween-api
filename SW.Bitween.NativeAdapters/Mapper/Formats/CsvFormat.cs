using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Reads and writes delimited text — comma, semicolon, pipe or tab.
/// </summary>
/// <remarks>
/// <para>
/// A file in this format <em>is</em> a list of rows, and that is the whole reason it needs so little
/// here. A list rule walking <c>""</c> already means "the document is itself the list", and
/// <c>Root</c> already means "the whole output is a list" — both written for a JSON document that is
/// a bare array, both already tested. So the mapper is untouched: this is a reader and a writer, and
/// everything between them already exists.
/// </para>
/// <para>
/// Every field is read as text and left alone. Nothing looks at <c>041800</c> and decides it is a
/// number, because a tracking reference that loses its leading zero is rejected by the partner and
/// nothing here would ever say why. Where the output wants a real number the rule's own type does
/// it, and it does it because someone asked rather than because something guessed.
/// </para>
/// <para>
/// An object output — not a list — is written as a header and exactly one row. That falls out of
/// treating a row as an object rather than being a case of its own.
/// </para>
/// </remarks>
public class CsvFormat(CsvOptions? options = null) : IDocumentFormat
{
    private readonly CsvOptions _options = options ?? new CsvOptions();

    public string Id => "csv";

    public string ContentType => "text/csv";

    /// <summary>No: a file of one row is a list of one, and says so.</summary>
    /// <remarks>
    /// Unlike XML, there is no ambiguity to resolve — a row is a row whether there is one of them or
    /// a thousand, so nothing has to be tolerated after the fact.
    /// </remarks>
    public bool SingleValueIsAList => false;

    /// <summary>
    /// The separator between a field's name and the name of a field inside it.
    /// </summary>
    /// <remarks>
    /// A row is flat and a mapping is not, so a rule writing to <c>destination.city</c> has to land
    /// somewhere. It becomes a column of exactly that name — which is how the editor already shows
    /// the path, and the only way to produce such a column at all, since the output-field name box
    /// splits what is typed into it on dots.
    /// </remarks>
    public const char PathSeparator = '.';

    public ValueNode Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DocumentFormatException("The document is empty, so there is nothing to map.");

        var list = ValueNode.List();

        try
        {
            using var reader = new StringReader(StripByteOrderMark(text));
            using var parser = new CsvParser(reader, Configuration(forWriting: false));

            var names = Array.Empty<string>();
            if (_options.HasHeader)
            {
                // A file that is nothing but a header is a list of no rows, which is a true answer
                // and not an error — a carrier with nothing to report sends exactly that.
                if (!parser.Read()) return list;
                names = ColumnNames(parser.Record ?? []);
            }

            while (parser.Read())
            {
                var record = parser.Record;
                if (record is null || IsBlank(record)) continue;

                var row = ValueNode.Object();
                for (var at = 0; at < record.Length; at++)
                    row.Set(NameAt(names, at), ValueNode.Value(record[at]));

                list.Add(row);
            }
        }
        catch (CsvHelperException ex)
        {
            throw new DocumentFormatException($"The document could not be read as delimited text: {ex.Message}");
        }

        return list;
    }

    public string Write(ValueNode root)
    {
        var rows = root switch
        {
            ListNode list => list.Items,
            // An object is one row. A mapping whose output is a single record has no reason to be
            // written as a list of one just to reach this format.
            ObjectNode => [root],
            _ => throw new DocumentFormatException(
                "A delimited file is rows of fields, so the output has to be a list or an object. " +
                "This mapping produced a single value."),
        };

        // Every column any row has, in the order they first appear. Rows can legitimately differ —
        // entries written into a list carry their own rules — and taking the first row's columns
        // would drop the rest without a word.
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var flattened = new List<Dictionary<string, string>>();

        foreach (var row in rows)
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(row, prefix: "", cells);
            foreach (var column in cells.Keys)
                if (seen.Add(column))
                    columns.Add(column);
            flattened.Add(cells);
        }

        var output = new StringWriter();
        using (var writer = new CsvWriter(output, Configuration(forWriting: true)))
        {
            if (_options.HasHeader)
            {
                foreach (var column in columns) writer.WriteField(column);
                writer.NextRecord();
            }

            foreach (var cells in flattened)
            {
                // A column this row does not have is written empty rather than skipped: a short row
                // would shift every field after it into the wrong column.
                foreach (var column in columns)
                    writer.WriteField(cells.TryGetValue(column, out var value) ? value : "");
                writer.NextRecord();
            }
        }

        // Prepended rather than written through the writer, which has no notion of one. Reading
        // strips it again, so a file we produce and then read back is unchanged by it.
        return _options.ByteOrderMark ? '\ufeff' + output.ToString() : output.ToString();
    }

    /// <summary>
    /// One row's cells, with nested fields folded into dotted column names.
    /// </summary>
    /// <remarks>
    /// A list inside a row is refused rather than folded. There is no column name that would make
    /// <c>["A1","B7"]</c> fit into one cell, and inventing one — joining them, taking the first —
    /// would lose data quietly, which is the one outcome worth refusing over.
    /// </remarks>
    private static void Flatten(ValueNode node, string prefix, Dictionary<string, string> cells)
    {
        switch (node)
        {
            case ObjectNode obj:
                foreach (var (key, child) in obj.Children())
                    Flatten(child, prefix.Length == 0 ? key : prefix + PathSeparator + key, cells);
                break;

            // A row that is a single value — a list of plain values, which is a perfectly ordinary
            // one-column file of tracking numbers. It has no name to take, so it takes the name any
            // column has when nothing names it: its position.
            case ScalarNode scalar:
                cells[prefix.Length == 0 ? Position(0) : prefix] = AsText(scalar.Value);
                break;

            case ListNode:
                throw new DocumentFormatException(
                    prefix.Length == 0
                        ? "A row of a delimited file cannot itself be a list."
                        : $"'{prefix}' is a list, and a single cell of a delimited file cannot hold " +
                          "one. Write its entries into named fields instead.");
        }
    }

    /// <summary>
    /// The parser and writer settings.
    /// </summary>
    /// <param name="forWriting">
    /// Whether to pin the line ending. CsvHelper applies <c>NewLine</c> on reading only when it has
    /// been set explicitly, and setting it there would refuse a file that separates its rows with
    /// bare newlines — which most of them do. So it is pinned for writing, where RFC 4180 asks for
    /// CRLF, and left alone for reading, where whatever the partner sent has to be accepted.
    /// </param>
    private CsvConfiguration Configuration(bool forWriting)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = _options.Delimiter,
            // Read by hand through the parser rather than mapped onto a class, so CsvHelper is never
            // asked to find a header; the names are taken from the first record here instead.
            HasHeaderRecord = false,
            // A partner file is not a well-formed file. A stray quote in the middle of a field is
            // common enough that refusing the whole document over one would stop a day's shipments,
            // and the field still arrives — as the characters that are actually there.
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false,
            // Whitespace is data. A name written as `s ramanan` and a postcode with a leading space
            // both turned up in the first files anyone sent.
            TrimOptions = TrimOptions.None,
            IgnoreBlankLines = true,
            // RFC 4180's set. A field carrying the delimiter, a quote or a line break has to be
            // quoted or the file it lands in no longer says what it meant to say.
            ShouldQuote = args =>
                args.Field is not null &&
                (args.Field.Contains(_options.Delimiter, StringComparison.Ordinal) ||
                 args.Field.Contains('"') ||
                 args.Field.Contains('\r') ||
                 args.Field.Contains('\n')),
        };

        // Assigned only for writing, because assigning it at all is what counts as setting it —
        // and a reader pinned to one line ending refuses every file that uses the other.
        if (forWriting) configuration.NewLine = "\r\n";

        return configuration;
    }

    /// <summary>
    /// The column names a header record gives, with every column left addressable.
    /// </summary>
    /// <remarks>
    /// A blank name and a repeated one both fall back to the column's position, which is the name
    /// that column would have had with no header at all. The alternative is two columns sharing a
    /// name, and since a row is built by setting keys on an object, the second would silently
    /// replace the first — a column of the partner's file simply missing, with nothing to say so.
    /// </remarks>
    private static string[] ColumnNames(string[] header)
    {
        var names = new string[header.Length];
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (var at = 0; at < header.Length; at++)
        {
            var name = header[at];
            names[at] = name.Length > 0 && used.Add(name) ? name : Position(at);
            used.Add(names[at]);
        }

        return names;
    }

    /// <summary>
    /// What the field at <paramref name="at"/> is called.
    /// </summary>
    /// <remarks>
    /// Past the end of the header — a row with more fields than the header named — the position is
    /// used, so the extra field is still readable. Dropping it would be the same silent loss as a
    /// duplicate name.
    /// </remarks>
    private static string NameAt(string[] names, int at) =>
        at < names.Length ? names[at] : Position(at);

    /// <summary>A field's name when it has none: its position, counting from one.</summary>
    private static string Position(int at) => (at + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a record is a blank line rather than a row.
    /// </summary>
    /// <remarks>
    /// <c>IgnoreBlankLines</c> covers an empty line, but a line holding only delimiters — which the
    /// samples have between blocks of records — parses as a row of empty fields. Mapping that would
    /// produce a row of nothing for every gap in the file.
    /// </remarks>
    private static bool IsBlank(string[] record) => record.All(field => field.Length == 0);

    /// <summary>
    /// Drops the byte-order mark, if the text still carries one.
    /// </summary>
    /// <remarks>
    /// Excel writes one, and left in place it becomes part of the first column's name — so a header
    /// of <c>ShipmentNumber</c> arrives as <c>﻿ShipmentNumber</c> and every rule reading it
    /// resolves to nothing, with the editor showing a name that looks exactly right.
    /// </remarks>
    private static string StripByteOrderMark(string text) =>
        text.Length > 0 && text[0] == '﻿' ? text[1..] : text;

    /// <summary>
    /// A value as a field's text.
    /// </summary>
    /// <remarks>
    /// Invariant throughout: a decimal written under a French locale uses a comma, which in a
    /// comma-delimited file would silently become an extra column. A decimal keeps the scale it was
    /// given, so a weight of <c>0.100</c> is written back as <c>0.100</c>.
    /// </remarks>
    private static string AsText(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
