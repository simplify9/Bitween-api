namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>How one side of a mapping reads or writes an Excel workbook (.xlsx).</summary>
public class ExcelOptions
{
    /// <summary>The sheet to read, or to name when writing. The first sheet, or <c>Sheet1</c>, when empty.</summary>
    public string? Sheet { get; set; }

    /// <summary>Whether the first row names the columns. Without it, columns are named by position: 1, 2, 3.</summary>
    public bool HasHeader { get; set; } = true;
}
