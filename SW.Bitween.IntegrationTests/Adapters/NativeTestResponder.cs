using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Adapters;

/// <summary>
/// A delivery that answers with whatever its <c>Body</c> property says, marked bad when <c>Bad</c>
/// is true — a bad response otherwise needs an HTTP endpoint that answers 4xx.
/// </summary>
public class NativeTestResponder : INativeInfolinkHandler
{
    private IDictionary<string, string> _settings = new Dictionary<string, string>();

    public string Name => nameof(NativeTestResponder);
    public Type StartupValuesType => typeof(object);

    public void InitializeStartupValues(IDictionary<string, string> settings) => _settings = settings;

    public Task<XchangeFile> Handle(XchangeFile xchangeFile) => Task.FromResult(new XchangeFile(
        _settings.TryGetValue("Body", out var body) ? body : xchangeFile.Data,
        badData: _settings.TryGetValue("Bad", out var bad) && bool.Parse(bad)));
}
