using System.ComponentModel;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>Settings the mapper is configured with.</summary>
/// <remarks>
/// <see cref="MappingRules"/> is the whole mapping. There is no template setting, because nothing is
/// generated — the rules are read and applied directly.
/// </remarks>
public class NativeMapperInput
{
    [Description("The mapping rules, as JSON. Written by the mapping editor.")]
    public string MappingRules { get; set; } = "";

    [Description("Sample source document, kept for the editor. Never read while mapping.")]
    public string SourceSample { get; set; } = "";

    [Description("Sample target document, kept for the editor. Never read while mapping.")]
    public string TargetSample { get; set; } = "";
}

/// <summary>
/// Maps a document from one format to another by applying stored rules.
/// </summary>
/// <remarks>
/// <para>
/// The class name is the adapter id stored on every subscription, and the pipeline decides
/// native-versus-serverless by testing whether that id starts with <c>native</c>. So the name has
/// to keep its prefix: an id of <c>Mapper</c> would be sent to the serverless path and fail to
/// start.
/// </para>
/// <para>
/// Runs as read → map → write. The middle step is format-neutral, so adding a format means adding
/// an <see cref="IDocumentFormat"/> and nothing else.
/// </para>
/// <para>
/// Marked <see cref="IReceivesMappingContext"/>, so the pipeline leaves the payload alone instead of
/// writing <c>__partner__</c> and <c>__globals__</c> into it. That is what allows a document which
/// is not JSON to be mapped at all.
/// </para>
/// </remarks>
public class NativeMapper : INativeInfolinkMapper, IReceivesMappingContext
{
    /// <summary>
    /// The settings key the pipeline passes the mapping context under.
    /// </summary>
    /// <remarks>
    /// Runtime context travels in the settings dictionary, which is how the exchange id already
    /// reaches an adapter (<c>mapperProperties["xchangeid"]</c>). The dictionary is a copy built per
    /// exchange, so nothing here is persisted with the subscription.
    /// </remarks>
    public const string ContextKey = "__mappingcontext";

    private MappingRules _rules = new();
    private MappingContext _context = MappingContext.Empty;

    public string Name => nameof(NativeMapper);

    public Type StartupValuesType => typeof(NativeMapperInput);

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _rules = ReadRules(settings);
        _context = ReadContext(settings);
    }

    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var source = ResolveFormat(_rules.SourceFormat, "source", _rules.SourceCsv);
        var target = ResolveFormat(_rules.TargetFormat, "target", _rules.TargetCsv);

        var input = source.Read(xchangeFile.Data);
        var output = DocumentMapper.Map(_rules, input, _context, source.SingleValueIsAList);

        return Task.FromResult(new XchangeFile(target.Write(output), xchangeFile.Filename)
        {
            ContentType = target.ContentType,
        });
    }

    private static MappingRules ReadRules(IDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(nameof(NativeMapperInput.MappingRules), out var json) ||
            string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException(
                "This mapper has no mapping rules configured. Open it in the mapping editor and save.");

        MappingRules? rules;
        try
        {
            rules = JsonConvert.DeserializeObject<MappingRules>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The mapping rules could not be read: {ex.Message}");
        }

        if (rules is null)
            throw new InvalidOperationException("The mapping rules could not be read: the value was empty.");

        // A rules document from a future release may use fields this build does not know about, and
        // silently ignoring them would map a subset of what the user configured.
        if (rules.Version > MappingRules.CurrentVersion)
            throw new InvalidOperationException(
                $"These mapping rules are version {rules.Version}, but this version of Bitween " +
                $"understands up to version {MappingRules.CurrentVersion}.");

        return rules;
    }

    /// <summary>
    /// Reads the context the pipeline passed, or an empty one.
    /// </summary>
    /// <remarks>
    /// Absent context is not an error — a mapping with no partner rules and no global rules needs
    /// none, and a preview may run without one. A rule that then asks for a partner value gets null,
    /// which is the same answer it would get for a partner with no such property.
    /// </remarks>
    private static MappingContext ReadContext(IDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(ContextKey, out var json) || string.IsNullOrWhiteSpace(json))
            return MappingContext.Empty;

        try
        {
            return JsonConvert.DeserializeObject<MappingContext>(json) ?? MappingContext.Empty;
        }
        catch (JsonException)
        {
            // The pipeline writes this, not a user. A malformed value means a bug rather than bad
            // configuration, and losing the partner values is better than failing every exchange.
            return MappingContext.Empty;
        }
    }

    private static IDocumentFormat ResolveFormat(string id, string role, CsvOptions? csv) =>
        DocumentFormats.TryGet(id, out var format, csv)
            ? format
            : throw new InvalidOperationException(DocumentFormats.Unsupported(id, role));
}
