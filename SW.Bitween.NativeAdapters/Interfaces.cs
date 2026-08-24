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
/// always registered — the key is a setting that can change at runtime — but they're kept out
/// of the adapter pickers while no key is set.
/// </summary>
public interface IRequiresRebexLicense { }

public interface INativeInfolinkHandler : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkMapper : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkValidator : IInfolinkValidator, INativeAdapter { }
public interface INativeInfolinkReceiver : IInfolinkReceiver, INativeAdapter { }
