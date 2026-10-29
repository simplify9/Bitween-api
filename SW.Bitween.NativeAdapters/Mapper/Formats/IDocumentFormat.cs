namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Everything a document format needs: how to read one into a tree, and how to write a tree back.
/// </summary>
/// <remarks>
/// The two halves are independent. Mapping XML to JSON uses one format's reader and another's
/// writer, so <c>xml→json</c>, <c>json→xml</c> and <c>xml→xml</c> are the same two pieces in
/// different combinations rather than three features.
/// </remarks>
public interface IDocumentFormat
{
    /// <summary>Stable id stored in the rules, e.g. <c>json</c>.</summary>
    string Id { get; }

    /// <summary>What to report for a document this format produced.</summary>
    /// <remarks>
    /// Set on the output <c>XchangeFile</c>. The previous mapper left it unset, and the gateway falls
    /// back to <c>application/json</c> - so an XML document would have been served to a partner as
    /// JSON.
    /// </remarks>
    string ContentType { get; }

    /// <summary>
    /// Whether a single value has to be walked as a list of one.
    /// </summary>
    /// <remarks>
    /// A format that makes a list by repeating a name - XML - cannot tell a list of one from a
    /// value that was never a list, so a list rule over such a path must walk what it found rather
    /// than treat it as no entries at all. JSON says which, so it says no.
    /// </remarks>
    bool SingleValueIsAList { get; }

    /// <summary>Reads a document into a tree.</summary>
    /// <exception cref="DocumentFormatException">When the text is not a valid document of this format.</exception>
    ValueNode Read(string text);

    /// <summary>Writes a tree out as a document.</summary>
    string Write(ValueNode root);
}

/// <summary>A document that could not be read as the format it was declared to be.</summary>
public class DocumentFormatException(string message) : Exception(message);
