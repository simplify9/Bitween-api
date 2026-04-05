using SW.Bitween.NativeAdapters.JsonMapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public class NativeJSONMapper : INativeInfolinkHandler
{
    public string Name => "NativeJSONMapper";
    public Type StartupValuesType => typeof(JsonMapperInput);

    private JsonMapperInput _options = new();

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = new JsonMapperInput
        {
            ScribanTemplate = settings.TryGetValue("ScribanTemplate", out var t) ? t : "{}"
        };
    }

    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var outputJson = ScribanJsonHelper.Render(_options.ScribanTemplate, xchangeFile.Data);
        return Task.FromResult(new XchangeFile(outputJson));
    }
}
