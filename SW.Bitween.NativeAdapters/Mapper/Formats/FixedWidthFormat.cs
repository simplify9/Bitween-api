using System.Globalization;
using System.Text;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Reads and writes fixed-width text: one record per line, each field at a set position.
/// </summary>
/// <remarks>
/// <para>
/// Like delimited text, a file in this format is a list of rows, so a list rule walking <c>""</c>
/// reads it and <c>Root</c> writes it, and the mapper itself is untouched.
/// </para>
/// <para>
/// A value too long for its column is refused rather than cut. A cut amount or reference is wrong in
/// a way nobody downstream can see, and the partner's file would be accepted and booked.
/// </para>
/// </remarks>
public class FixedWidthFormat(FixedWidthOptions? options) : IDocumentFormat
{
    private readonly FixedWidthOptions _options = options ?? new FixedWidthOptions();

    public string Id => "fixedwidth";

    public string ContentType => "text/plain";

    public bool SingleValueIsAList => false;

    public ValueNode Read(string text)
    {
        var columns = Checked();
        if (string.IsNullOrEmpty(text))
            throw new DocumentFormatException("The document is empty, so there is nothing to map.");

        var list = ValueNode.List();
        var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines.Skip(Math.Max(0, _options.SkipLines)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var row = ValueNode.Object();
            foreach (var column in columns)
            {
                var start = column.Start - 1;
                // A line shorter than the layout is read as far as it goes: trailing blank fields
                // are routinely dropped by the systems that write these files.
                var value = start >= line.Length ? "" : line.Substring(start, Math.Min(column.Length, line.Length - start));
                row.Set(column.Name, ValueNode.Value(_options.Trim ? value.Trim() : value));
            }
            list.Add(row);
        }

        return list;
    }

    public string Write(ValueNode root)
    {
        var columns = Checked();
        var rows = root switch
        {
            ListNode list => list.Items,
            ObjectNode => [root],
            _ => throw new DocumentFormatException(
                "A fixed-width file is records of fields, so the output has to be a list or an object. " +
                "This mapping produced a single value."),
        };

        var width = columns.Max(c => c.Start - 1 + c.Length);
        var output = new StringBuilder();
        foreach (var row in rows)
        {
            if (row is not ObjectNode record)
                throw new DocumentFormatException("Each record of a fixed-width file has to be an object of named fields.");

            // Nested fields become dotted names, as in delimited text, so destination.city can be a column.
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(record, "", cells);
            var known = columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var unknown = cells.Keys.FirstOrDefault(k => !known.Contains(k));
            if (unknown != null)
                throw new DocumentFormatException(
                    $"The mapping writes '{unknown}', which isn't one of the file's columns. Add it to the layout, or don't write it.");

            var line = new char[width];
            Array.Fill(line, ' ');
            foreach (var column in columns)
            {
                var value = cells.GetValueOrDefault(column.Name, "");
                if (value.Length > column.Length)
                    throw new DocumentFormatException(
                        $"'{value}' is {value.Length} characters, and the column '{column.Name}' holds {column.Length}. " +
                        "Shorten it in the mapping, or widen the column if the specification allows.");

                var pad = string.IsNullOrEmpty(column.Pad) ? ' ' : column.Pad[0];
                var cell = string.Equals(column.Align, "right", StringComparison.OrdinalIgnoreCase)
                    ? value.PadLeft(column.Length, pad)
                    : value.PadRight(column.Length, pad);
                cell.CopyTo(0, line, column.Start - 1, column.Length);
            }
            output.Append(line).Append('\n');
        }

        return output.ToString();
    }

    private static void Flatten(ValueNode node, string prefix, Dictionary<string, string> cells)
    {
        switch (node)
        {
            case ObjectNode obj:
                foreach (var (key, child) in obj.Children())
                    Flatten(child, prefix.Length == 0 ? key : prefix + "." + key, cells);
                break;
            case ScalarNode scalar:
                cells[prefix] = Text(scalar.Value);
                break;
            case ListNode:
                throw new DocumentFormatException(
                    $"'{prefix}' is a list, and a field of a fixed-width file cannot hold one. Write its entries into named fields instead.");
        }
    }

    /// <summary>The columns in order, refused when they can't describe a file.</summary>
    private List<FixedWidthColumn> Checked()
    {
        var columns = (_options.Columns ?? []).OrderBy(c => c.Start).ToList();
        if (columns.Count == 0)
            throw new DocumentFormatException("A fixed-width layout needs its columns: a name, a start and a length for each.");

        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            if (string.IsNullOrWhiteSpace(c.Name))
                throw new DocumentFormatException($"The column starting at {c.Start} has no name.");
            if (c.Start < 1 || c.Length < 1)
                throw new DocumentFormatException($"The column '{c.Name}' needs a start of 1 or more and a length of 1 or more.");
            if (i > 0 && columns[i - 1].Start - 1 + columns[i - 1].Length > c.Start - 1)
                throw new DocumentFormatException($"The columns '{columns[i - 1].Name}' and '{c.Name}' overlap.");
        }

        var duplicate = columns.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new DocumentFormatException($"Two columns are named '{duplicate.Key}'.");
        return columns;
    }

    internal static string Text(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
