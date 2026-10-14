#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests.PayloadParsing;

/// <summary>
/// The file the pipeline hands from step to step with its parsed trees (finding 6c) is, to anything
/// outside the process, the file it always was.
/// </summary>
[TestClass]
public class ParsedXchangeFileTests
{
    private const string Payload = "{\"id\":\"A-1\",\"price\":1.10}";

    [TestMethod]
    public void It_serializes_exactly_as_the_file_it_extends()
    {
        var plain = new XchangeFile(Payload, "order.json") { ContentType = "application/json" };
        var parsed = new ParsedXchangeFile(Payload, "order.json") { ContentType = "application/json" };
        parsed.Payload.TryGet(ParseProfiles.Defaults, parsed.Data, out _);

        Assert.AreEqual(JsonConvert.SerializeObject(plain), JsonConvert.SerializeObject(parsed));
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(plain), System.Text.Json.JsonSerializer.Serialize<object>(parsed));
    }

    /// <summary>A resident or classic adapter is sent text; only the native runtime gets the trees.</summary>
    [TestMethod]
    public async Task A_runtime_outside_the_process_is_handed_a_plain_file()
    {
        var runtime = new Recording();
        var parsed = new ParsedXchangeFile(Payload, "order.json", badData: true) { ContentType = "text/plain" };

        await new AdapterInvoker([runtime]).InvokeAsync<XchangeFile>("resident.thing", AdapterRole.Handler, "Handle", parsed);

        var handed = (XchangeFile)runtime.Argument!;
        Assert.AreEqual(typeof(XchangeFile), handed.GetType());
        Assert.AreEqual(parsed.Data, handed.Data);
        Assert.AreEqual(parsed.Filename, handed.Filename);
        Assert.AreEqual(parsed.Hash, handed.Hash);
        Assert.AreEqual(parsed.BadData, handed.BadData);
        Assert.AreEqual(parsed.ContentType, handed.ContentType);
    }

    [TestMethod]
    public void A_tree_is_handed_out_only_for_the_text_it_was_parsed_from()
    {
        var parsed = new ParsedXchangeFile(Payload);
        var sameCharacters = new string(Payload.ToCharArray());

        Assert.IsTrue(parsed.Payload.TryGet(ParseProfiles.Defaults, parsed.Data, out _));
        Assert.IsFalse(parsed.Payload.TryGet(ParseProfiles.Defaults, sameCharacters, out _));
    }

    [TestMethod]
    public void Each_profile_is_parsed_once_and_a_failed_parse_is_not_shared()
    {
        var tally = PayloadParseTally.Open();
        var parsed = new ParsedXchangeFile("{\"price\":1.10} trailing");

        Assert.IsFalse(parsed.Payload.TryGet(ParseProfiles.Defaults, parsed.Data, out _));
        Assert.IsFalse(parsed.Payload.TryGet(ParseProfiles.Defaults, parsed.Data, out _));
        Assert.AreEqual(1, tally.Count);

        var good = new ParsedXchangeFile(Payload);
        Assert.IsTrue(good.Payload.TryGet(ParseProfiles.Defaults, good.Data, out var first));
        Assert.IsTrue(good.Payload.TryGet(ParseProfiles.Defaults, good.Data, out var second));
        Assert.IsTrue(good.Payload.TryGet(ParseProfiles.Exact, good.Data, out var exact));
        Assert.AreSame(first, second);
        Assert.AreNotSame(first, exact);
        Assert.AreEqual(3, tally.Count);
    }

    private sealed class Recording : IAdapterRuntime
    {
        public object? Argument { get; private set; }

        public Task<bool> CanRunAsync(string adapterId) => Task.FromResult(true);

        public Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
            IDictionary<string, string> properties, string correlationId) =>
            Task.FromResult<IAdapterSession>(new Session(this));

        private sealed class Session(Recording owner) : IAdapterSession
        {
            public Task<TResult> InvokeAsync<TResult>(string method, object? argument = null)
            {
                owner.Argument = argument;
                return Task.FromResult(default(TResult)!);
            }

            public Task InvokeAsync(string method, object? argument = null)
            {
                owner.Argument = argument;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
