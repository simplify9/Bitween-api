extern alias OpenXml;

using System.Globalization;
using System.Text.RegularExpressions;
using OpenXml::DocumentFormat.OpenXml;
using OpenXml::DocumentFormat.OpenXml.Packaging;
using OpenXml::DocumentFormat.OpenXml.Spreadsheet;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Reads and writes Excel workbooks (.xlsx): one sheet's rows, the way delimited text is rows.
/// </summary>
/// <remarks>
/// <para>
/// A workbook is binary, and a message in Bitween is text, so it travels as base64: a receiver with
/// <c>ResponseEncoding</c> <c>base64</c> hands one over that way, and an upload handler with
/// <c>DataEncoding</c> <c>base64</c> writes the bytes back out. Read, the document is a list of rows
/// as CSV's is, so the mapper is untouched.
/// </para>
/// <para>
/// Cells are read as text, as CSV's are, so a reference keeps its leading zeros: numbers in their
/// invariant form, dates (cells formatted as dates) as ISO 8601, booleans as <c>true</c> or
/// <c>false</c>. Formulas are read as the value Excel last calculated. The older .xls format isn't read.
/// </para>
/// </remarks>
public class ExcelFormat(ExcelOptions? options) : IDocumentFormat
{
    private readonly ExcelOptions _options = options ?? new ExcelOptions();

    public string Id => "excel";

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public bool SingleValueIsAList => false;

    public ValueNode Read(string text)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String((text ?? "").Trim());
        }
        catch (FormatException)
        {
            throw new DocumentFormatException(
                "An Excel workbook travels as base64, and this isn't. Set the receiver's ResponseEncoding to base64.");
        }

        if (bytes.Length == 0)
            throw new DocumentFormatException("The document is empty, so there is nothing to map.");

        SpreadsheetDocument workbook;
        try
        {
            workbook = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FileFormatException)
        {
            throw new DocumentFormatException("The document isn't an Excel workbook (.xlsx). An older .xls file has to be saved as .xlsx first.");
        }

        using (workbook)
        {
            var book = workbook.WorkbookPart ?? throw new DocumentFormatException("The workbook has no sheets.");
            var sheets = book.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
            var sheet = string.IsNullOrWhiteSpace(_options.Sheet)
                ? sheets.FirstOrDefault()
                : sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, _options.Sheet, StringComparison.OrdinalIgnoreCase));
            if (sheet?.Id?.Value == null)
                throw new DocumentFormatException(string.IsNullOrWhiteSpace(_options.Sheet)
                    ? "The workbook has no sheets."
                    : $"The workbook has no sheet named '{_options.Sheet}'. It has: {string.Join(", ", sheets.Select(s => s.Name?.Value))}.");

            var part = (WorksheetPart)book.GetPartById(sheet.Id.Value);
            var strings = book.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
            var dates = DateStyles(book);

            var list = ValueNode.List();
            List<string>? names = _options.HasHeader ? null : [];
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
            {
                var cells = new SortedDictionary<int, string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var value = ValueOf(cell, strings, dates);
                    if (value.Length > 0) cells[ColumnIndex(cell.CellReference?.Value)] = value;
                }
                if (cells.Count == 0) continue;

                if (names == null)
                {
                    names = [];
                    var width = cells.Keys.Max() + 1;
                    for (var at = 0; at < width; at++)
                        names.Add(Unique(cells.TryGetValue(at, out var name) && name.Length > 0 ? name : (at + 1).ToString(CultureInfo.InvariantCulture), used));
                    continue;
                }

                while (names.Count <= cells.Keys.Max())
                    names.Add(Unique((names.Count + 1).ToString(CultureInfo.InvariantCulture), used));
                var record = ValueNode.Object();
                for (var at = 0; at < names.Count; at++)
                    record.Set(names[at], ValueNode.Value(cells.GetValueOrDefault(at, "")));
                list.Add(record);
            }

            return list;
        }
    }

    public string Write(ValueNode root)
    {
        var rows = root switch
        {
            ListNode list => list.Items,
            ObjectNode => [root],
            _ => throw new DocumentFormatException(
                "A worksheet is rows of fields, so the output has to be a list or an object. This mapping produced a single value."),
        };

        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var flattened = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var cells = new Dictionary<string, object?>(StringComparer.Ordinal);
            Flatten(row, "", cells);
            foreach (var column in cells.Keys)
                if (seen.Add(column)) columns.Add(column);
            flattened.Add(cells);
        }

        using var stream = new MemoryStream();
        using (var workbook = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = workbook.AddWorkbookPart();
            book.Workbook = new Workbook();
            var part = book.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            part.Worksheet = new Worksheet(data);

            uint number = 1;
            if (_options.HasHeader)
                data.Append(RowOf(number++, columns.Select(c => (object?)c).ToList()));
            foreach (var cells in flattened)
                data.Append(RowOf(number++, columns.Select(c => cells.GetValueOrDefault(c)).ToList()));

            var sheets = book.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = book.GetIdOfPart(part),
                SheetId = 1,
                Name = string.IsNullOrWhiteSpace(_options.Sheet) ? "Sheet1" : _options.Sheet,
            });
            book.Workbook.Save();
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    private static Row RowOf(uint number, List<object?> values)
    {
        var row = new Row { RowIndex = number };
        for (var at = 0; at < values.Count; at++)
        {
            var reference = ColumnName(at) + number.ToString(CultureInfo.InvariantCulture);
            row.Append(values[at] switch
            {
                null => new Cell { CellReference = reference },
                bool b => new Cell { CellReference = reference, DataType = CellValues.Boolean, CellValue = new CellValue(b ? "1" : "0") },
                decimal or double or float or int or long =>
                    new Cell { CellReference = reference, CellValue = new CellValue(Convert.ToString(values[at], CultureInfo.InvariantCulture)!) },
                // Text stays text: a reference with leading zeros must not turn into a number.
                var other => new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(FixedWidthFormat.Text(other)) { Space = SpaceProcessingModeValues.Preserve }),
                },
            });
        }
        return row;
    }

    private static void Flatten(ValueNode node, string prefix, Dictionary<string, object?> cells)
    {
        switch (node)
        {
            case ObjectNode obj:
                foreach (var (key, child) in obj.Children())
                    Flatten(child, prefix.Length == 0 ? key : prefix + "." + key, cells);
                break;
            case ScalarNode scalar:
                if (!cells.TryAdd(prefix.Length == 0 ? "1" : prefix, scalar.Value))
                    throw new DocumentFormatException($"Two rules both write the column '{prefix}'. Give one of them another name.");
                break;
            case ListNode:
                throw new DocumentFormatException(prefix.Length == 0
                    ? "A row of a worksheet cannot itself be a list."
                    : $"'{prefix}' is a list, and a single cell cannot hold one. Write its entries into named fields instead.");
        }
    }

    private static string ValueOf(Cell cell, List<string> strings, HashSet<uint> dates)
    {
        var raw = cell.CellValue?.Text ?? "";
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString)
            return int.TryParse(raw, out var at) && at >= 0 && at < strings.Count ? strings[at] : "";
        if (type == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";
        if (type == CellValues.Boolean)
            return raw == "1" ? "true" : "false";
        if (type == CellValues.String || type == CellValues.Error)
            return raw;

        if (cell.StyleIndex?.Value is { } style && dates.Contains(style)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            var when = DateTime.FromOADate(serial);
            return when.TimeOfDay == TimeSpan.Zero
                ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : when.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return raw;
    }

    /// <summary>The cell formats that show a number as a date or a time.</summary>
    private static HashSet<uint> DateStyles(WorkbookPart book)
    {
        var styles = book.WorkbookStylesPart?.Stylesheet;
        var custom = styles?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.NumberFormatId?.Value != null && IsDateCode(f.FormatCode?.Value))
            .Select(f => f.NumberFormatId!.Value).ToHashSet() ?? [];
        var result = new HashSet<uint>();
        var formats = styles?.CellFormats?.Elements<CellFormat>().ToList() ?? [];
        for (var at = 0; at < formats.Count; at++)
        {
            var id = formats[at].NumberFormatId?.Value ?? 0;
            if (id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58 || custom.Contains(id))
                result.Add((uint)at);
        }
        return result;
    }

    private static readonly Regex Literal = new("\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", RegexOptions.Compiled);

    internal static bool IsDateCode(string? code) =>
        !string.IsNullOrEmpty(code) && Literal.Replace(code, "").IndexOfAny(['d', 'm', 'y', 'h', 's', 'D', 'M', 'Y', 'H', 'S']) >= 0;

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return 0;
        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c)) break;
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{name}_{n}";
        return candidate;
    }
}
