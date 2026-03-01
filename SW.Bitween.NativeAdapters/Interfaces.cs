using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public interface INativeAdapter
{
    public string Name { get; }
    public void InitializeStartupValues(IDictionary<string, string> settings);
    public Type StartupValuesType { get; }
}

public interface INativeInfolinkHandler : INativeAdapter,IInfolinkHandler { }
public interface INativeInfolinkValidator: IInfolinkValidator,INativeAdapter{}
public interface INativeInfolinkReceiver: IInfolinkReceiver,INativeAdapter{}
