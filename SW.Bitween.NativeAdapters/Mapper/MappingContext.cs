namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// The values a mapping can use that do not come out of the document being mapped.
/// </summary>
/// <remarks>
/// Passed alongside the payload rather than written into it. The previous approach — writing
/// <c>__partner__</c> and <c>__globals__</c> keys into the incoming JSON — meant the payload had to
/// be parsed as JSON before anything knew which mapper was configured, so a non-JSON document could
/// not be mapped at all; a document with a real key of the same name lost it; and only a JSON object
/// was enriched, so the same mapping behaved differently for a root array.
/// </remarks>
public class MappingContext
{
    /// <summary>The exchange's partner's adapter properties, by key. Empty when there is no partner.</summary>
    public IReadOnlyDictionary<string, string> Partner { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Global values sets, by set id then key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Globals { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Values read from the original document — the input of the exchange whose delivery got the
    /// response being mapped — by path. Empty
    /// unless a response or bus gateway subscription is being mapped.
    /// </summary>
    public IReadOnlyDictionary<string, string> Source { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Id of the exchange being mapped, for diagnostics.</summary>
    public string? XchangeId { get; init; }

    public static MappingContext Empty { get; } = new();

    public string? PartnerValue(string? key) =>
        key is not null && Partner.TryGetValue(key, out var value) ? value : null;

    public string? SourceValue(string? path) =>
        path is not null && Source.TryGetValue(path, out var value) ? value : null;

    public string? GlobalValue(string? setId, string? key)
    {
        if (setId is null || key is null) return null;
        return Globals.TryGetValue(setId, out var set) && set.TryGetValue(key, out var value)
            ? value
            : null;
    }
}

/// <summary>One rule that could not be applied, and why.</summary>
/// <param name="Target">The rule's target path, as the user sees it.</param>
/// <param name="Reason">What went wrong, in terms of the rule rather than the implementation.</param>
public readonly record struct MappingError(string Target, string Reason)
{
    public override string ToString() => $"{Target}: {Reason}";
}

/// <summary>
/// Thrown when any rule fails, carrying every failure rather than only the first.
/// </summary>
/// <remarks>
/// A mapping with two broken rules used to surface as one exception naming neither, so fixing it
/// meant re-running to find the next one. Failing the exchange rather than nulling the field is
/// deliberate: a partner silently receiving a document with holes in it is worse than a partner
/// receiving nothing and someone being told why.
/// </remarks>
public class MappingFailedException : Exception
{
    public MappingFailedException(IReadOnlyList<MappingError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<MappingError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<MappingError> errors)
    {
        var count = errors.Count == 1 ? "1 rule" : $"{errors.Count} rules";
        return $"Mapping failed — {count} could not be applied: " +
               string.Join("; ", errors.Select(e => e.ToString()));
    }
}
