#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.JsonMapper;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.Bitween.UnitTests.PayloadParsing.Golden;
using SW.PrimitiveTypes;
using RulesMapper = SW.Bitween.NativeAdapters.Mapper.NativeMapper;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;

namespace SW.Bitween.UnitTests.PayloadParsing;

/// <summary>
/// Compiled Scriban templates and mapping rules kept by their text: the same object for
/// the same text, nothing kept that failed, a bound, a switch that turns it off, and the two things
/// that make sharing one object between exchanges safe: a run does not change the rules, and one
/// template renders correctly from many threads at once.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CompiledCacheTests
{
    [TestCleanup]
    public void SwitchBackOn() => CompiledTemplates.Enabled = true;

    [TestMethod]
    public void The_same_template_text_compiles_once_while_the_cache_is_on()
    {
        var text = "{ \"id\": {{ id | json }}, \"once\": " + Guid.NewGuid().ToString("N").Length + " }";

        Assert.AreSame(ScribanJsonHelper.CompiledTemplate(text), ScribanJsonHelper.CompiledTemplate(text));
    }

    [TestMethod]
    public void With_the_cache_off_every_call_compiles_its_own_template()
    {
        CompiledTemplates.Enabled = false;
        var text = "{ \"id\": {{ id | json }}, \"off\": 1 }";

        Assert.AreNotSame(ScribanJsonHelper.CompiledTemplate(text), ScribanJsonHelper.CompiledTemplate(text));
    }

    [TestMethod]
    public void A_template_with_errors_is_not_kept_and_still_fails_every_call()
    {
        var before = ScribanJsonHelper.Templates.Count;
        const string broken = "{ \"id\": {{ id | }} ";

        var first = Assert.ThrowsException<InvalidOperationException>(() => ScribanJsonHelper.Render(broken, "{\"id\":1}"));
        var second = Assert.ThrowsException<InvalidOperationException>(() => ScribanJsonHelper.Render(broken, "{\"id\":1}"));

        Assert.AreEqual(first.Message, second.Message);
        Assert.AreEqual(before, ScribanJsonHelper.Templates.Count);
    }

    [TestMethod]
    public void The_same_rules_text_is_read_once_while_the_cache_is_on()
    {
        var settings = new Dictionary<string, string> { [nameof(NativeMapperInput.MappingRules)] = RichRules };

        Assert.AreSame(RulesMapper.ReadRules(settings), RulesMapper.ReadRules(settings));

        CompiledTemplates.Enabled = false;
        Assert.AreNotSame(RulesMapper.ReadRules(settings), RulesMapper.ReadRules(settings));
    }

    [TestMethod]
    public void Rules_that_cannot_be_read_are_not_kept()
    {
        var before = RulesMapper.Rules.Count;
        var settings = new Dictionary<string, string> { [nameof(NativeMapperInput.MappingRules)] = "{\"Version\": 99}" };

        Assert.ThrowsException<InvalidOperationException>(() => RulesMapper.ReadRules(settings));
        Assert.ThrowsException<InvalidOperationException>(() => RulesMapper.ReadRules(settings));
        Assert.AreEqual(before, RulesMapper.Rules.Count);
    }

    [TestMethod]
    public void The_least_recently_used_entry_goes_once_the_cache_is_full()
    {
        var cache = new CompiledCache<object>(2);
        var compiled = 0;
        object Compile(string _) { compiled++; return new object(); }

        var a = cache.GetOrAdd("a", Compile);
        cache.GetOrAdd("b", Compile);
        Assert.AreSame(a, cache.GetOrAdd("a", Compile));
        cache.GetOrAdd("c", Compile);

        Assert.AreEqual(2, cache.Count);
        Assert.AreSame(a, cache.GetOrAdd("a", Compile), "a was used last, so b went");
        cache.GetOrAdd("b", Compile);
        Assert.AreEqual(4, compiled);
    }

    /// <summary>
    /// Every input of the golden corpus mapped by rules that use every kind of rule, with the cached
    /// rules compared, as JSON, before and after. A mapper that wrote to its rules would leak one
    /// exchange's data into the next.
    /// </summary>
    [TestMethod]
    public async Task A_mapping_run_does_not_change_the_cached_rules()
    {
        foreach (var rulesText in new[] { RichRules, GoldenConsumers.JsonRules, GoldenConsumers.XmlRules })
        {
            var settings = new Dictionary<string, string> { [nameof(NativeMapperInput.MappingRules)] = rulesText };
            var cached = RulesMapper.ReadRules(settings);
            var before = JsonConvert.SerializeObject(cached);

            foreach (var (_, input) in GoldenPayloadTests.Corpus())
            {
                var mapper = new RulesMapper();
                mapper.InitializeStartupValues(settings);
                try
                {
                    await mapper.Handle(new XchangeFile(input));
                }
                catch (Exception ex) when (ex is DocumentFormatException or InvalidOperationException or MappingFailedException)
                {
                    // An input this side of the rules cannot read; the rules must still be untouched.
                }
            }

            Assert.AreSame(cached, RulesMapper.ReadRules(settings));
            Assert.AreEqual(before, JsonConvert.SerializeObject(cached));
        }
    }

    /// <summary>
    /// One cached template, 32 threads, each rendering its own model many times over, every output
    /// compared with what a template compiled for that call alone renders.
    /// </summary>
    [TestMethod]
    public void One_cached_template_renders_correctly_from_32_threads_with_different_models()
    {
        const int threads = 32;
        const int rounds = 40;
        var template = GoldenConsumers.ScribanTemplate;
        var models = Enumerable.Range(0, threads).Select(Model).ToArray();

        CompiledTemplates.Enabled = false;
        var expected = models.Select(m => ScribanJsonHelper.Render(template, m)).ToArray();
        Assert.AreEqual(threads, expected.Distinct().Count(), "every model should render differently");
        CompiledTemplates.Enabled = true;

        var shared = ScribanJsonHelper.CompiledTemplate(template);
        var start = new Barrier(threads);
        var wrong = new List<string>();
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            for (var round = 0; round < rounds; round++)
            {
                var output = ScribanJsonHelper.Render(template, models[i]);
                if (output != expected[i])
                    lock (wrong) wrong.Add($"thread {i}, round {round}:\n{output}");
            }
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());

        Assert.AreSame(shared, ScribanJsonHelper.CompiledTemplate(template));
        Assert.AreEqual(0, wrong.Count, string.Join("\n\n", wrong.Take(3)));

        static string Model(int i) => new JObject
        {
            ["id"] = $"T-{i}",
            ["price"] = i + 0.25,
            ["orderedAt"] = new DateTime(2026, 10, 1 + i % 28, i % 24, 0, 0, DateTimeKind.Utc),
            ["name"] = new string((char)('a' + i % 26), i + 1),
            ["order"] = new JObject
            {
                ["lines"] = new JArray(Enumerable.Range(0, i % 7).Select(n => new JObject { ["sku"] = $"S{i}-{n}", ["qty"] = n })),
            },
            ["__partner__"] = i % 2 == 0 ? new JObject { ["since"] = $"partner {i}", ["rate"] = i } : null,
        }.ToString(Formatting.None);
    }

    /// <summary>Rules with every kind of rule in them, including the ones holding JSON of their own.</summary>
    private static readonly string RichRules = JsonConvert.SerializeObject(new MappingRules
    {
        Fields =
        [
            Field("id", "id"),
            Field("price", "price", ValueType.Number),
            new FieldRule
            {
                Target = ["channels"],
                From = new ValueSource { Kind = ValueSourceKind.Fixed, Value = new JArray("Email", "Sms") },
            },
            new FieldRule
            {
                Target = ["contact"],
                From = new ValueSource { Kind = ValueSourceKind.Fixed, Value = new JObject { ["kind"] = "ops", ["tags"] = new JArray("a") } },
            },
            new FieldRule
            {
                Target = ["status"],
                From = new ValueSource { Kind = ValueSourceKind.Path, Path = "id" },
                Lookup = new LookupRule { Table = new() { ["A-1"] = "known", ["R-1"] = new JArray(1, 2) }, Fallback = "other" },
            },
            new FieldRule
            {
                Target = ["name"],
                From = new ValueSource { Kind = ValueSourceKind.Path, Path = "name" },
                Transform = new TransformRule { Fn = "replace", Args = { ["find"] = "a", ["with"] = "b" } },
            },
            new FieldRule { Target = ["partnerCode"], From = new ValueSource { Kind = ValueSourceKind.Partner, Key = "code" } },
        ],
        Lists =
        [
            new ListRule
            {
                Over = "order.lines", As = "line", Target = ["lines"],
                Where = new FilterRule { Field = "qty", Operator = FilterOperator.GreaterThan, Value = 0 },
                Fields = [Field("sku", "sku"), Field("qty", "qty", ValueType.Number)],
                Fixed = [new ListEntry { Fields = [new FieldRule { Target = ["sku"], From = new ValueSource { Kind = ValueSourceKind.Fixed, Value = "HEADER" } }] }],
                After = [new ListEntry { Fields = [new FieldRule { Target = ["sku"], From = new ValueSource { Kind = ValueSourceKind.Fixed, Value = new JObject { ["end"] = true } } }] }],
            },
        ],
    });

    private static FieldRule Field(string target, string path, ValueType? type = null) =>
        new() { Target = [target], From = new ValueSource { Kind = ValueSourceKind.Path, Path = path }, Type = type };
}
