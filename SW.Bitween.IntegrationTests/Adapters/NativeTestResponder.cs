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

    /// <summary>How many times each <c>Body</c> was delivered, so a test can tell a repeat delivery happened.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Deliveries = new();

    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var hasBody = _settings.TryGetValue("Body", out var body);
        if (hasBody) Deliveries.AddOrUpdate(body, 1, (_, n) => n + 1);

        return Task.FromResult(new XchangeFile(
            hasBody ? body : xchangeFile.Data,
            badData: _settings.TryGetValue("Bad", out var bad) && bool.Parse(bad)));
    }
}
