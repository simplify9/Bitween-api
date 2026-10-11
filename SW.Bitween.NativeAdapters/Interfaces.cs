using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public interface INativeAdapter
{
    public string Name { get; }
    public void InitializeStartupValues(IDictionary<string, string> settings);
    public Type StartupValuesType { get; }
}

/// <summary>
/// Marks an adapter that only works with a Rebex license key configured. Such adapters are
/// always registered - the key is a setting that can change at runtime - but they're kept out
/// of the adapter pickers while no key is set.
/// </summary>
public interface IRequiresRebexLicense { }

/// <summary>
/// Marks a mapper that is given the partner and global values as context of its own, rather than
/// reading them out of the payload.
/// </summary>
/// <remarks>
/// <para>
/// Partner and global values reach a mapper by being written <em>into</em> the incoming document as
/// <c>__partner__</c> and <c>__globals__</c> keys (see <c>XchangeService.RunMapper</c>). That costs
/// more than it looks: the payload has to be parsed as JSON before anything knows which mapper is
/// configured, so a non-JSON document cannot get that far; any document with a real key of the same
/// name loses it; and only a JSON object is enriched, so the same mapping behaves differently for a
/// root array.
/// </para>
/// <para>
/// A mapper marked this way is handed the values separately and the payload is left exactly as it
/// arrived - which is the only way a format other than JSON can be mapped at all.
/// </para>
/// </remarks>
public interface IReceivesMappingContext { }

public interface INativeInfolinkHandler : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkMapper : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkValidator : IInfolinkValidator, INativeAdapter { }
public interface INativeInfolinkReceiver : IInfolinkReceiver, INativeAdapter { }
