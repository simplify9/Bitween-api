namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// How one side of a mapping reads or writes delimited text.
/// </summary>
/// <remarks>
/// <para>
/// Kept per side rather than per mapping: a partner's semicolon file is routinely turned into a
/// comma file for somebody else, and one client alone has been seen sending all three of comma,
/// semicolon and pipe.
/// </para>
/// <para>
/// There is no standard worth the name for any of this, which is why none of it is inferred.
/// Sniffing the delimiter from a sample gets it wrong the first time a field legitimately contains
/// a comma, and by then the mapping is in production.
/// </para>
/// </remarks>
public class CsvOptions
{
    /// <summary>The characters between one field and the next.</summary>
    /// <remarks>
    /// A string rather than a char because a tab travels through JSON as <c>\t</c>, and because
    /// multi-character delimiters exist in the wild.
    /// </remarks>
    public string Delimiter { get; set; } = ",";

    /// <summary>
    /// Whether the first line names the columns rather than carrying data.
    /// </summary>
    /// <remarks>
    /// False is not the unusual case. Two of the three files a single client sends start straight
    /// into data, so the fields have no names at all and paths are positions instead — see
    /// <see cref="CsvFormat"/>.
    /// </remarks>
    public bool HasHeader { get; set; } = true;

    /// <summary>
    /// Whether to start a written file with a byte-order mark.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three bytes that tell Excel the text is UTF-8. Without them a name like
    /// <c>BEAUTRAIT Raphaël</c> opens as <c>RaphaÃ«l</c>, and whoever opens it has no way to
    /// correct that after the fact — which is why this has to be decided when the file is written.
    /// </para>
    /// <para>
    /// Off by default, because a partner's own parser can just as easily choke on three bytes it
    /// did not expect at the start of the file. Reading always strips one, whatever this says:
    /// left in place it becomes part of the first column's name.
    /// </para>
    /// </remarks>
    public bool ByteOrderMark { get; set; }
}
