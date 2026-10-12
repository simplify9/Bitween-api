namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>How one side of a mapping reads or writes fixed-width text: which column is where.</summary>
/// <remarks>
/// Nothing is inferred. A fixed-width file says nothing about where one field ends, so the columns
/// are exactly what the partner's specification says, as a 1-based start and a length.
/// </remarks>
public class FixedWidthOptions
{
    public List<FixedWidthColumn> Columns { get; set; } = [];

    /// <summary>Lines at the top that aren't records, such as a title or a header, skipped when reading.</summary>
    public int SkipLines { get; set; }

    /// <summary>Whether a read value has its padding taken off. On by default; off keeps every space.</summary>
    public bool Trim { get; set; } = true;
}

public class FixedWidthColumn
{
    /// <summary>The field's name, which paths in the mapping use.</summary>
    public string Name { get; set; } = "";

    /// <summary>Where the column starts, counting the line's first character as 1.</summary>
    public int Start { get; set; } = 1;

    public int Length { get; set; } = 1;

    /// <summary>
    /// Which side a written value keeps to: <c>left</c>, padded on the right, or <c>right</c>, padded on
    /// the left, as amounts usually are.
    /// </summary>
    public string Align { get; set; } = "left";

    /// <summary>The character padding a written value. A space unless the specification says otherwise, often <c>0</c> for numbers.</summary>
    public string Pad { get; set; } = " ";
}
