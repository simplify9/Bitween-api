namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>What one side of a mapping says about its format, for the formats that take settings.</summary>
public record FormatOptions(CsvOptions? Csv = null, FixedWidthOptions? FixedWidth = null, ExcelOptions? Excel = null);
