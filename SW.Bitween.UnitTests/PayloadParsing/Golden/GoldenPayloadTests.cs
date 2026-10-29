#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.JsonMapper;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.PrimitiveTypes;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;
using RulesMapper = SW.Bitween.NativeAdapters.Mapper.NativeMapper;

namespace SW.Bitween.UnitTests.PayloadParsing.Golden;

/// <summary>
/// What every consumer of a payload makes of it, held byte for byte against fixtures captured on the
/// code before finding 6 of docs/design/processing-performance.md changed how payloads are parsed.
/// </summary>
/// <remarks>
/// <para>
/// Each file in <c>inputs</c> goes through every consumer that reads a payload: promoted properties
/// and match results (<see cref="FilterService"/>, JSON and XML), the schema check
/// (<see cref="DocumentSchema"/>, JSON and XML), the rules-based mapper (JSON and XML), the Scriban
/// mapper, the enrichment that writes <c>__partner__</c> and <c>__globals__</c>, the enrichment
/// followed by the Scriban mapper as <c>RunMapper</c> chains them, the HTTP handler's templated URL,
/// and the file-name fill of the FTP and SMTP handlers. Each consumer runs on every input, so a JSON
/// consumer's answer to XML (an error) is held as well. A consumer that throws is held by its
/// exception's type and message.
/// </para>
/// <para>
/// The fixtures in <c>expected</c> are the proof that nothing a consumer produces has changed. They
/// are written, never edited: set <c>BITWEEN_GOLDEN_WRITE=1</c> to capture them, and only on the
/// code whose behaviour they are meant to hold.
/// </para>
/// <para>
/// Dates in a payload become local times when parsed with Newtonsoft's defaults, so the fixtures are
/// captured, and checked, in one fixed zone and the invariant culture.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class GoldenPayloadTests
{
    private const string Zone = "Asia/Tokyo";
    private static string? zoneBefore;

    [ClassInitialize]
    public static void FixTheZone(TestContext _)
    {
        zoneBefore = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", Zone);
        TimeZoneInfo.ClearCachedData();
    }

    [ClassCleanup]
    public static void RestoreTheZone()
    {
        Environment.SetEnvironmentVariable("TZ", zoneBefore);
        TimeZoneInfo.ClearCachedData();
    }

    private static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static string InputsFolder => Path.Combine(Folder(), "inputs");
    private static string ExpectedFolder => Path.Combine(Folder(), "expected");

    /// <summary>The corpus, read as bytes and decoded without dropping a byte-order mark.</summary>
    internal static IReadOnlyList<(string Name, string Text)> Corpus() =>
        Directory.GetFiles(InputsFolder)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), Encoding.UTF8.GetString(File.ReadAllBytes(f))))
            .ToList();

    /// <summary>
    /// With each of the parsing switches on, and off: either way, nothing may change. With the shared
    /// parse on, every consumer of an input is handed one <see cref="ParsedXchangeFile"/>, in the order
    /// the pipeline reaches them, as the worker hands its input from step to step; off, each gets a
    /// plain file and parses for itself, as before.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, true, DisplayName = "shared parse, compiled templates kept")]
    [DataRow(true, false, DisplayName = "shared parse, compiled templates not kept")]
    [DataRow(false, true, DisplayName = "own parses, compiled templates kept")]
    [DataRow(false, false, DisplayName = "own parses, compiled templates not kept")]
    public async Task Every_consumer_reads_the_corpus_as_it_did_before_finding_6(bool sharedParse, bool cacheCompiledTemplates)
    {
        Assert.AreEqual(TimeSpan.FromHours(9), TimeZoneInfo.Local.BaseUtcOffset,
            "The fixtures hold local times in " + Zone + ", and this process did not take the zone.");

        CompiledTemplates.Enabled = cacheCompiledTemplates;
        try
        {
            await CheckTheCorpus(sharedParse);
        }
        finally
        {
            CompiledTemplates.Enabled = true;
        }
    }

    /// <summary>
    /// The trees the consumers shared are the trees a fresh parse makes: no consumer wrote to one. The
    /// enrichment writes <c>__partner__</c> into every object it is handed, so a missing copy shows here.
    /// </summary>
    [TestMethod]
    public async Task No_consumer_changes_a_shared_tree()
    {
        foreach (var (name, text) in Corpus())
        {
            var file = new ParsedXchangeFile(text);
            await GoldenConsumers.RunAll(_ => file);

            if (file.Payload.TryGet(ParseProfiles.Defaults, file.Data, out var defaults))
                Assert.IsTrue(JToken.DeepEquals(defaults, JToken.Parse(text)), $"{name}: the defaults tree changed");
            if (file.Payload.TryGet(ParseProfiles.Exact, file.Data, out var exact))
                Assert.IsTrue(JToken.DeepEquals(exact, JsonFormat.ParseExact(text)), $"{name}: the exact tree changed");
            if (file.Payload.TryGet(ParseProfiles.RawXml, file.Data, out var raw))
                Assert.IsTrue(XNode.DeepEquals(raw, XmlFormat.LoadRaw(text)), $"{name}: the raw XML changed");
            if (file.Payload.TryGet(XmlExchangePayloadReader.Cleaned, file.Data, out var cleaned))
                Assert.IsTrue(XNode.DeepEquals(cleaned, XmlExchangePayloadReader.Cleaned.Parse(text)), $"{name}: the cleaned XML changed");
        }
    }

    private static async Task CheckTheCorpus(bool sharedParse)
    {
        var write = Environment.GetEnvironmentVariable("BITWEEN_GOLDEN_WRITE") == "1";
        var corpus = Corpus();
        Assert.IsTrue(corpus.Count >= 40, $"The corpus has {corpus.Count} inputs.");

        var mismatches = new List<string>();
        foreach (var (name, text) in corpus)
        {
            ParsedXchangeFile? shared = sharedParse ? new ParsedXchangeFile(text) : null;
            var actual = await GoldenConsumers.RunAll(_ => shared ?? new XchangeFile(text));
            var expectedPath = Path.Combine(ExpectedFolder, name + ".txt");

            if (write)
            {
                Directory.CreateDirectory(ExpectedFolder);
                await File.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(actual));
                continue;
            }

            Assert.IsTrue(File.Exists(expectedPath), $"No fixture for {name}.");
            var expected = await File.ReadAllBytesAsync(expectedPath);
            if (!expected.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(actual)))
                mismatches.Add(GoldenConsumers.FirstDifference(name, Encoding.UTF8.GetString(expected), actual));
        }

        if (write) Assert.Inconclusive($"Fixtures written for {corpus.Count} inputs; unset BITWEEN_GOLDEN_WRITE to check them.");
        Assert.AreEqual(0, mismatches.Count, string.Join("\n\n", mismatches));
    }
}

/// <summary>The consumers the golden test runs, each the way the pipeline calls it.</summary>
internal static class GoldenConsumers
{
    public const int JsonDocumentId = 1;
    public const int XmlDocumentId = 2;

    /// <summary>Promotes only an integer past long's range, which the reader cannot turn into text.</summary>
    public const int BigDocumentId = 3;

    /// <summary>The order the pipeline reaches them in, which matters once they share a parse.</summary>
    public static readonly (string Name, Func<XchangeFile, Task<string>> Run)[] All =
    [
        ("promoted-json", f => Promoted(JsonDocumentId, f)),
        ("promoted-xml", f => Promoted(XmlDocumentId, f)),
        ("promoted-json-big", f => Promoted(BigDocumentId, f)),
        ("schema-json", f => Schema(DocumentFormat.Json, JsonSchemaText, f)),
        ("schema-xml", f => Schema(DocumentFormat.Xml, XsdText, f)),
        ("enriched", Enriched),
        ("enriched-scriban", EnrichedThenScriban),
        ("scriban", Scriban),
        ("rules-json", f => Rules(JsonRules, f)),
        ("rules-xml", f => Rules(XmlRules, f)),
        ("url", Url),
        ("fill", Fill),
    ];

    /// <summary>Every consumer on the file <paramref name="fileFor"/> gives it, in pipeline order.</summary>
    public static async Task<string> RunAll(Func<string, XchangeFile> fileFor)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var output = new StringBuilder();
            foreach (var (name, run) in All)
            {
                output.Append("=== ").Append(name).Append('\n');
                string result;
                try
                {
                    result = await run(fileFor(name));
                }
                catch (Exception ex)
                {
                    result = $"!{ex.GetType().FullName}: {ex.Message}";
                }

                output.Append(result).Append('\n');
            }

            return output.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    public static string FirstDifference(string name, string expected, string actual)
    {
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;
        var from = Math.Max(0, at - 120);
        return $"{name} differs at character {at}:\n--- expected\n{Slice(expected)}\n--- actual\n{Slice(actual)}";

        string Slice(string s) => s.Substring(Math.Min(from, s.Length), Math.Min(240, Math.Max(0, s.Length - from)));
    }

    // ─── promoted properties and match results ──────────────────────────────────────────────

    private static readonly FilterService Filter = new(GoldenCache.Create());

    private static async Task<string> Promoted(int documentId, XchangeFile file)
    {
        var result = await Filter.Filter(documentId, file);
        var output = new StringBuilder();
        foreach (var (key, value) in result.Properties)
            output.Append(key).Append(" = ").Append(value ?? "(null)").Append('\n');
        output.Append("hits: ").Append(string.Join(", ", result.Hits)).Append('\n');
        output.Append("gateway: ").Append(string.Join(", ", result.GatewayHits.Select(h => $"{h.SubscriptionId}/{h.PartnerId}")));
        return output.ToString();
    }

    // ─── schema ─────────────────────────────────────────────────────────────────────────────

    private const string JsonSchemaText = """
        {
          "type": "object",
          "required": ["id"],
          "properties": {
            "id": { "type": "string" },
            "price": { "type": "number", "maximum": 100 },
            "orderedAt": { "type": "string", "format": "date-time" },
            "order": {
              "type": "object",
              "properties": {
                "lines": { "type": "array", "items": { "type": "object", "required": ["sku"] } }
              }
            }
          }
        }
        """;

    private const string XsdText = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="order">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="id" type="xs:string"/>
                <xs:element name="price" type="xs:decimal" minOccurs="0"/>
                <xs:element name="name" type="xs:string" minOccurs="0"/>
                <xs:element name="lines" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="line" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="sku" type="xs:string"/>
                            <xs:element name="qty" type="xs:int"/>
                          </xs:sequence>
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static async Task<string> Schema(DocumentFormat format, string schema, XchangeFile file)
    {
        var errors = await DocumentSchema.Check(format, schema, file);
        return errors.Count == 0 ? "(valid)" : string.Join("\n", errors);
    }

    // ─── enrichment, and the Scriban mapper after it ───────────────────────────────────────

    public static readonly Dictionary<string, string> PartnerProperties = new()
    {
        ["code"] = "P1",
        ["since"] = "2026-10-14T10:00:00Z",
        ["rate"] = "1.10",
        ["Upper"] = "U",
    };

    public static readonly GlobalAdapterValuesSet[] GlobalSets =
    [
        GoldenCache.WithId(new GlobalAdapterValuesSet { Name = "Set one", Values = new() { ["region"] = "KSA", ["cutoff"] = "2026-10-14" } }, "set1"),
        GoldenCache.WithId(new GlobalAdapterValuesSet { Name = "Empty", Values = new() }, "set2"),
    ];

    private static Task<string> Enriched(XchangeFile file)
    {
        var payload = MapperEnrichment.Read(file, out var shared);
        if (payload is null) return Task.FromResult("(not an object, not enriched)");
        return Task.FromResult(MapperEnrichment.Enrich(payload, shared, PartnerProperties, GlobalSets) ?? "(nothing to add)");
    }

    /// <summary>What <c>RunMapper</c> hands a Scriban mapper: the payload, enriched when it is an object.</summary>
    private static async Task<string> EnrichedThenScriban(XchangeFile file)
    {
        var payload = MapperEnrichment.Read(file, out var shared);
        if (payload != null && MapperEnrichment.Enrich(payload, shared, PartnerProperties, GlobalSets) is { } enriched)
            file = new XchangeFile(enriched, file.Filename);
        return await Scriban(file);
    }

    // ─── Scriban mapper ─────────────────────────────────────────────────────────────────────

    public const string ScribanTemplate = """
        {
          "id": {{ id | json }},
          "price": {{ price | json }},
          "priceText": "{{ price }}",
          "priceFloat": {{ price | to_float | json }},
          "orderedAt": {{ orderedAt | json }},
          "orderedAtText": "{{ orderedAt }}",
          "offset": {{ offset | json }},
          "ms": {{ ms | json }},
          "big": {{ big | json }},
          "exp": {{ exp | json }},
          "negzero": {{ negzero | json }},
          "name": {{ name | json }},
          "firstItem": {{ if items }}{{ items[0] | json }}{{ else }}null{{ end }},
          "lines": [{{ for l in order?.lines }}{"sku": {{ l.sku | json }}, "qty": {{ l.qty | json }}, "price": {{ l.price | json }}},{{ end }}],
          "lineCount": {{ order?.lines | array.size }},
          "partner": {{ __partner__ | json }},
          "partnerSince": "{{ __partner__?.since }}",
          "partnerRate": {{ __partner__?.rate | json }},
          "region": {{ __globals__?.set1?.region | json }},
          "orderId": {{ orderId | json }},
          "upper.name": {{ name | json }},
        }
        """;

    private static async Task<string> Scriban(XchangeFile file)
    {
        var mapper = new NativeJSONMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string> { ["ScribanTemplate"] = ScribanTemplate });
        return (await mapper.Handle(file)).Data;
    }

    // ─── rules-based mapper ─────────────────────────────────────────────────────────────────

    private static FieldRule Field(string target, string path, ValueType? type = null, TransformRule? transform = null) =>
        new() { Target = [target], From = new ValueSource { Kind = ValueSourceKind.Path, Path = path }, Type = type, Transform = transform };

    private static TransformRule Fn(string name, params (string, object)[] args)
    {
        var rule = new TransformRule { Fn = name };
        foreach (var (k, v) in args) rule.Args[k] = JToken.FromObject(v);
        return rule;
    }

    public static readonly string JsonRules = JsonConvert.SerializeObject(new MappingRules
    {
        Fields =
        [
            Field("id", "id"),
            Field("price", "price"),
            Field("priceNumber", "price", ValueType.Number),
            Field("priceText", "price", ValueType.String),
            Field("priceRounded", "price", transform: Fn("round", ("decimals", 1))),
            Field("big", "big"),
            Field("exp", "exp"),
            Field("negzero", "negzero"),
            Field("negzerof", "negzerof"),
            Field("tenthTimesThree", "tenth", transform: Fn("multiply", ("by", 3))),
            Field("orderedAt", "orderedAt"),
            Field("orderedAtFormatted", "orderedAt", transform: Fn("formatDate", ("format", "yyyy-MM-dd HH:mm"))),
            Field("offset", "offset"),
            Field("dmy", "dmy"),
            Field("name", "name", transform: Fn("upper")),
            Field("flag", "flag", ValueType.Boolean),
            new FieldRule { Target = ["fixed"], From = new ValueSource { Kind = ValueSourceKind.Fixed, Value = 1.10m } },
        ],
        Lists =
        [
            new ListRule
            {
                Over = "order.lines", As = "line", Target = ["lines"],
                Fields = [Field("sku", "sku"), Field("qty", "qty"), Field("price", "price")],
            },
        ],
    });

    public static readonly string XmlRules = JsonConvert.SerializeObject(new MappingRules
    {
        SourceFormat = "xml",
        Fields =
        [
            Field("id", "order.id"),
            Field("price", "order.price"),
            Field("priceNumber", "order.price", ValueType.Number),
            Field("currency", "order.price.@currency"),
            Field("when", "order.when"),
            Field("name", "order.name"),
        ],
        Lists =
        [
            new ListRule
            {
                Over = "order.lines.line", As = "line", Target = ["lines"],
                Fields = [Field("sku", "sku"), Field("qty", "qty", ValueType.Number)],
            },
        ],
    });

    private static async Task<string> Rules(string rules, XchangeFile file)
    {
        var mapper = new RulesMapper();
        mapper.InitializeStartupValues(new Dictionary<string, string> { [nameof(NativeMapperInput.MappingRules)] = rules });
        var output = await mapper.Handle(file);
        return output.Data + "\ncontent-type: " + output.ContentType;
    }

    // ─── HTTP handler's templated URL ───────────────────────────────────────────────────────

    public const string UrlTemplate =
        "https://partner.test/orders/{{ id }}?price={{ price }}&at={{ orderedAt }}&name={{ name }}&big={{ big }}&sku={{ order.lines[0].sku }}";

    private static async Task<string> Url(XchangeFile file)
    {
        var server = new Capture();
        var handler = new NativeHttpHandler(new Proxy(new HttpClient(server)));
        handler.InitializeStartupValues(new Dictionary<string, string> { ["Url"] = UrlTemplate, ["Verb"] = "get" });
        await handler.Handle(file);
        return server.Uri!.OriginalString + "\n" + server.Uri.AbsoluteUri;
    }

    private sealed class Proxy(HttpClient client) : IDynamicHttpProxy
    {
        public HttpClient GetClient(string fullUrl) => client;
    }

    private sealed class Capture : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    // ─── file-name fill of the FTP and SMTP handlers ────────────────────────────────────────

    private static Task<string> Fill(XchangeFile file) =>
        Task.FromResult(ScribanJsonHelper.FillIfJson("order-{{ id }}-{{ price }}-{{ orderedAt }}.json", file.Data));
}

/// <summary>The information types, subscriptions and routes the promoted-property consumer reads.</summary>
public class GoldenCache : DispatchProxy
{
    private readonly Dictionary<int, Document> documents = new();
    private readonly Dictionary<int, Subscription[]> subscriptions = new();
    private readonly Dictionary<int, BusGatewayRoute[]> routes = new();

    internal static IInfolinkCache Create()
    {
        var cache = DispatchProxy.Create<IInfolinkCache, GoldenCache>();
        ((GoldenCache)(object)cache).Fill();
        return cache;
    }

    private void Fill()
    {
        var json = new Document(GoldenConsumers.JsonDocumentId, "Golden JSON", DocumentFormat.Json);
        json.SetDictionaries(new Dictionary<string, string>
        {
            ["id"] = "id",
            ["price"] = "$.price",
            ["orderedAt"] = "orderedAt",
            ["offset"] = "offset",
            ["name"] = "name",
            ["sku"] = "order.lines[0].sku",
            ["lines"] = "order.lines",
            ["negzero"] = "negzero",
            ["exp"] = "exp",
            ["dotted"] = "['order.id']",
            ["missing"] = "nope.not.here",
        });
        documents[json.Id] = json;
        subscriptions[json.Id] =
        [
            Internal(101, new OneOfSpec("price", ["1.1"])),
            Internal(102, new OneOfSpec("orderedAt", ["10/14/2026 19:00:00", "10/14/2026 16:00:00"])),
            Internal(103, new NotOneOfSpec("id", ["X-2"])),
            Internal(104, null),
            Internal(105, new OrSpec(new OneOfSpec("order.lines[0].sku", ["S1"]), new OneOfSpec("name", ["Acme Retail"]))),
            Internal(106, new AndSpec(new OneOfSpec("exp", ["100000"]), new OneOfSpec("negzero", ["0"]))),
        ];
        routes[json.Id] =
        [
            new BusGatewayRoute { SubscriptionId = 501, PartnerId = 7, MatchExpression = new OneOfSpec("id", ["A-1", "R-1", "T-1"]) },
            new BusGatewayRoute { SubscriptionId = 502, PartnerId = null, MatchExpression = null },
        ];

        var big = new Document(GoldenConsumers.BigDocumentId, "Golden big", DocumentFormat.Json);
        big.SetDictionaries(new Dictionary<string, string> { ["big"] = "big" });
        documents[big.Id] = big;
        subscriptions[big.Id] = [Internal(301, new OneOfSpec("big", ["12345678901234567890"]))];
        routes[big.Id] = [];

        var xml = new Document(GoldenConsumers.XmlDocumentId, "Golden XML", DocumentFormat.Xml);
        xml.SetDictionaries(new Dictionary<string, string>
        {
            ["id"] = "/*[local-name()='order']/*[local-name()='id']",
            ["price"] = "//*[local-name()='price']",
            ["name"] = "/order/name",
            ["sku"] = "//*[local-name()='line'][1]/*[local-name()='sku']",
            ["missing"] = "/order/nope",
        });
        documents[xml.Id] = xml;
        subscriptions[xml.Id] =
        [
            Internal(201, new OneOfSpec("//*[local-name()='price']", ["1.10"])),
            Internal(202, new OneOfSpec("/order/id", ["P-1", "A & B"])),
            Internal(203, null),
        ];
        routes[xml.Id] =
        [
            new BusGatewayRoute { SubscriptionId = 601, PartnerId = 8, MatchExpression = new OneOfSpec("/order/name", ["Acme Retail", "Acme"]) },
        ];
    }

    private static Subscription Internal(int id, IPropertyMatchSpecification? expression)
    {
        var subscription = WithId(new Subscription($"Golden {id}", id / 100, SubscriptionType.Internal, partnerId: 9), id);
        if (expression != null) subscription.SetMatchExpression(expression);
        return subscription;
    }

    internal static T WithId<T>(T entity, object id)
    {
        for (var type = typeof(T); type != null; type = type.BaseType)
        {
            var property = type.GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property?.GetSetMethod(true) is { } setter)
            {
                setter.Invoke(entity, [id]);
                return entity;
            }

            var field = type.GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(entity, id);
                return entity;
            }
        }

        throw new InvalidOperationException($"{typeof(T).Name} has no Id to set.");
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IInfolinkCache.DocumentByIdAsync) => Task.FromResult(documents[(int)args![0]!]),
        nameof(IInfolinkCache.ListSubscriptionsByDocumentAsync) => Task.FromResult(subscriptions[(int)args![0]!]),
        nameof(IInfolinkCache.ListBusGatewayRoutesByDocumentAsync) => Task.FromResult(routes[(int)args![0]!]),
        _ => throw new NotSupportedException($"The golden cache does not answer {method?.Name}."),
    };
}
