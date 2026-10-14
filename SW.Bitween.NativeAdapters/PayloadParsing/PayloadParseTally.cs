namespace SW.Bitween.NativeAdapters;

/// <summary>
/// The full parses of a payload into a tree (a <c>JToken</c>, an <c>XDocument</c>, a dictionary)
/// that one stage of one exchange made: its creation on a gateway call, its processing, its result
/// lane.
/// </summary>
/// <remarks>
/// How the work on parsing is measured (processing-performance.md, finding 6). Opened with the
/// stage's database command tally, which records both, and counted wherever a payload is parsed:
/// the payload readers, the schema check, the enrichment, the built-in mappers and the HTTP
/// handler's URL model. Lives here rather than beside the command tally because the built-in
/// adapters count into it and cannot see the API project.
/// </remarks>
public sealed class PayloadParseTally
{
    static readonly AsyncLocal<PayloadParseTally?> current = new();

    int count;

    PayloadParseTally()
    {
    }

    /// <summary>The tally of the stage running on this flow, if one was opened.</summary>
    public static PayloadParseTally? Current => current.Value;

    /// <summary>
    /// Opens a tally for what runs on this flow from here on. Called at the top of an async method,
    /// it covers that method and everything it awaits, and ends with it.
    /// </summary>
    public static PayloadParseTally Open()
    {
        var tally = new PayloadParseTally();
        current.Value = tally;
        return tally;
    }

    public int Count => Volatile.Read(ref count);

    /// <summary>One parse of a payload into a tree, counted against the tally open on this flow.</summary>
    public static void Parsed()
    {
        if (current.Value is { } tally) Interlocked.Increment(ref tally.count);
    }
}
