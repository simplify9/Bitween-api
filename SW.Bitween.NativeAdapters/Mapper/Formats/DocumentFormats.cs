using System.Diagnostics.CodeAnalysis;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// The formats a mapping can read and write, by the id stored in its rules.
/// </summary>
/// <remarks>
/// One registry, so the adapter and the preview endpoint cannot disagree about what is supported —
/// a format the editor offers but the pipeline rejects would be a mapping that previews and then
/// fails. Adding a format is one entry here plus the reader and writer it names.
/// </remarks>
public static class DocumentFormats
{
    private static readonly JsonFormat Json = new();
    private static readonly XmlFormat Xml = new();

    /// <summary>
    /// How each id is built.
    /// </summary>
    /// <remarks>
    /// Factories rather than instances because delimited text is configured per side — a partner's
    /// semicolon file routinely becomes somebody else's comma file — so its reader and its writer
    /// cannot be the same shared object. The formats that need no configuration stay singletons.
    /// </remarks>
    private static readonly Dictionary<string, Func<CsvOptions?, IDocumentFormat>> ById =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["json"] = _ => Json,
            ["xml"] = _ => Xml,
            ["csv"] = csv => new CsvFormat(csv),
        };

    /// <summary>Format ids, for the editor's dropdown and for error messages.</summary>
    public static IReadOnlyList<string> Ids { get; } = ById.Keys.OrderBy(k => k).ToList();

    /// <summary>The format for an id, configured with <paramref name="csv"/> where it applies.</summary>
    public static bool TryGet(
        string? id,
        [NotNullWhen(true)] out IDocumentFormat? format,
        CsvOptions? csv = null)
    {
        format = null;
        if (id is null || !ById.TryGetValue(id, out var build)) return false;

        format = build(csv);
        return true;
    }

    /// <summary>
    /// The message for an id no format answers to, naming what is available.
    /// </summary>
    /// <param name="role">Either <c>source</c> or <c>target</c>, so the reader knows which end.</param>
    public static string Unsupported(string? id, string role) =>
        $"'{id}' is not a {role} format this mapper supports. Supported: {string.Join(", ", Ids)}.";
}
