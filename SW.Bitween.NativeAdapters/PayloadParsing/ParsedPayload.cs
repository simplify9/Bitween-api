using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// One way of parsing a payload into a tree. Consumers share a parse only through the same profile,
/// so every consumer gets a tree parsed exactly as it parsed one for itself.
/// </summary>
/// <remarks>
/// Profiles are compared by identity. The parse is the consumer's own code, moved here unchanged,
/// and the consumer still calls it itself whenever it is not handed a shared tree.
/// </remarks>
internal sealed class ParseProfile<T>(string name, Func<string, T> parse) where T : class
{
    public string Name => name;

    public T Parse(string text) => parse(text);

    public override string ToString() => name;
}

/// <summary>The two JSON profiles and the raw XML one.</summary>
/// <remarks>
/// The cleaned XML profile of the promoted-property reader lives beside that reader in the API.
/// </remarks>
internal static class ParseProfiles
{
    /// <summary>
    /// Newtonsoft's defaults, as <c>JToken.Parse</c> does: floats become <c>double</c>, ISO dates
    /// become <c>DateTime</c>, trailing content is refused. The promoted-property reader, the match
    /// expressions, the schema check, the enrichment and the Scriban model read this one.
    /// </summary>
    public static readonly ParseProfile<JToken> Defaults = new("defaults", text => JToken.Parse(text));

    /// <summary>
    /// <c>FloatParseHandling.Decimal</c>, <c>DateParseHandling.None</c> and the trailing-content check
    /// of the rules-based mapper's <see cref="JsonFormat"/>, so money stays exact and dates stay text.
    /// </summary>
    public static readonly ParseProfile<JToken> Exact = new("exact", JsonFormat.ParseExact);

    /// <summary>The XML as the rules-based mapper's <see cref="XmlFormat"/> loads it: raw, no DTD, no resolver.</summary>
    public static readonly ParseProfile<XDocument> RawXml = new("raw xml", XmlFormat.LoadRaw);
}

/// <summary>
/// The trees one payload has been parsed into, one per profile, each parsed on first ask and kept for
/// the rest of the exchange's run on this node.
/// </summary>
/// <remarks>
/// <para>
/// The trees are shared, so they are never written to: a consumer that changes what it read (the
/// enrichment) clones first. A parse that fails is not shared: the consumer parses for itself, as
/// it always did, and fails with exactly the error it always gave.
/// </para>
/// <para>
/// A tree is handed out only for the very text it was parsed from, by reference, so a consumer
/// reading some other text, such as a mapper's output, can never be given the input's tree.
/// </para>
/// </remarks>
internal sealed class ParsedPayload(string text)
{
    static readonly object Failed = new();

    readonly Dictionary<object, Lazy<object>> slots = new();

    /// <summary>The payload of <paramref name="file"/> when the pipeline handed a parsed one; null otherwise.</summary>
    public static ParsedPayload? Of(XchangeFile? file) => (file as ParsedXchangeFile)?.Payload;

    /// <summary>
    /// The tree <paramref name="profile"/> makes of <paramref name="forText"/>, parsed once and counted
    /// once. False when <paramref name="forText"/> is not this payload's text or the parse failed;
    /// the caller then parses for itself.
    /// </summary>
    public bool TryGet<T>(ParseProfile<T> profile, string? forText, out T tree) where T : class
    {
        tree = null!;
        if (forText is null || !ReferenceEquals(forText, text)) return false;

        Lazy<object> slot;
        lock (slots)
        {
            if (!slots.TryGetValue(profile, out slot!))
                slots[profile] = slot = new Lazy<object>(() =>
                {
                    PayloadParseTally.Parsed();
                    try
                    {
                        return profile.Parse(text);
                    }
                    catch
                    {
                        return Failed;
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        if (slot.Value is not T parsed) return false;
        tree = parsed;
        return true;
    }
}

/// <summary>
/// An exchange file that carries its payload's parsed trees, so the pipeline's steps and the built-in
/// adapters parse it once per profile rather than once each.
/// </summary>
/// <remarks>
/// <see cref="XchangeFile"/> is in a package and cannot change, so the trees ride on a subclass that
/// adds nothing public: serialized, it is the same JSON as the file it extends. Only the native
/// runtime passes it on; an adapter that runs out of process is handed <see cref="ToPlain"/>, the
/// text alone, as it always was. Made where the file is first made, so its hash is computed once.
/// </remarks>
internal sealed class ParsedXchangeFile : XchangeFile
{
    public ParsedXchangeFile(string data, string? filename = null, bool badData = false)
        : base(data, filename, badData)
    {
        Payload = new ParsedPayload(Data);
    }

    internal ParsedPayload Payload { get; }

    /// <summary>The same file without the trees: what leaves the process.</summary>
    internal XchangeFile ToPlain() => new(Data, Filename, BadData) { ContentType = ContentType };

    /// <summary>An argument as an adapter outside this process gets it: a parsed file as plain text.</summary>
    internal static object? Plain(object? argument) => argument is ParsedXchangeFile parsed ? parsed.ToPlain() : argument;
}
