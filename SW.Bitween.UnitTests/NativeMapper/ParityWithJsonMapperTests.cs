using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// The same mapping scenarios <c>ScribanJsonHelper*Tests</c> cover for the old mapper, expressed as
/// rules and asserted on the output document.
/// </summary>
/// <remarks>
/// <para>
/// This is the parity proof for phase 1. It deliberately asserts on documents rather than on
/// generated template text: what a user cares about is the document a partner receives, and the old
/// mapper's tests that assert on template strings describe an implementation this one does not have.
/// </para>
/// <para>
/// Four groups of the old tests have no equivalent here, on purpose:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>TrailingCommas_AreRemovedFromRenderedJson</c> and <c>InvalidScribanTemplate</c> - there is no
/// template, so neither situation exists.
/// </description></item>
/// <item><description>
/// <c>SmartArray_AllowsFirstItemMemberAccessWithoutIndex</c> - a list no longer pretends to be its
/// own first element. That was ambiguous: <c>order.line.sku</c> silently meant the first line's sku,
/// with no way to ask for anything else. A path into a list belongs to a loop.
/// </description></item>
/// <item><description>
/// <c>SourceMapping_ResolvesLowercaseAliasForPascalCaseKey</c> - every key no longer gets a
/// camelCase twin. A path matches the document exactly.
/// </description></item>
/// <item><description>
/// The TypeScript round-trip tests - they test parsing a template back into rules, which is the
/// thing this design removes.
/// </description></item>
/// </list>
/// </remarks>
[TestClass]
public class ParityWithJsonMapperTests
{
    private static readonly JsonFormat Json = new();

    private static JToken Run(MappingRules rules, string input, MappingContext? context = null) =>
        JToken.Parse(Json.Write(DocumentMapper.Map(rules, Json.Read(input), context ?? MappingContext.Empty)));

    private static FieldRule Field(string target, ValueSource from, ValueType? type = null,
        TransformRule? transform = null, LookupRule? lookup = null) =>
        new() { Target = [target], From = from, Type = type, Transform = transform, Lookup = lookup };

    private static ValueSource Path(string path) => new() { Kind = ValueSourceKind.Path, Path = path };
    private static ValueSource Fixed(object? value) => new() { Kind = ValueSourceKind.Fixed, Value = value };

    private static TransformRule Fn(string name, params (string, object)[] args)
    {
        var rule = new TransformRule { Fn = name };
        foreach (var (k, v) in args) rule.Args[k] = JToken.FromObject(v);
        return rule;
    }

    // ── source mappings ─────────────────────────────────────────────────────────

    /// <summary>Old: <c>SourceMapping_RenamesFlatFields</c>.</summary>
    [TestMethod]
    public void RenamesFlatFields()
    {
        var output = Run(
            new MappingRules { Fields = [Field("orderId", Path("OrderId")), Field("name", Path("Customer"))] },
            """{ "OrderId": "A1", "Customer": "Ali" }""");

        Assert.AreEqual("A1", output["orderId"]?.ToString());
        Assert.AreEqual("Ali", output["name"]?.ToString());
    }

    /// <summary>Old: <c>SourceMapping_ReadsNestedField</c>.</summary>
    [TestMethod]
    public void ReadsNestedField() =>
        Assert.AreEqual("Amman",
            Run(new MappingRules { Fields = [Field("city", Path("customer.address.city"))] },
                """{ "customer": { "address": { "city": "Amman" } } }""")["city"]?.ToString());

    /// <summary>
    /// Old: <c>SourceMapping_ExpandsDottedOutputPath</c>. The old mapper split a dotted target
    /// string; here the nesting is explicit segments, which also means a key may contain a dot.
    /// </summary>
    [TestMethod]
    public void BuildsNestedOutput()
    {
        var rules = new MappingRules
        {
            Fields = [new FieldRule { Target = ["customer", "address", "city"], From = Path("city") }],
        };

        Assert.AreEqual("Amman", Run(rules, """{ "city": "Amman" }""")["customer"]?["address"]?["city"]?.ToString());
    }

    /// <summary>Old: <c>SourceMapping_ResolvesPascalCaseKeyAsIs</c>.</summary>
    [TestMethod]
    public void MatchesTheDocumentsOwnCasing()
    {
        var output = Run(
            new MappingRules { Fields = [Field("a", Path("CustomerId")), Field("b", Path("customerId"))] },
            """{ "CustomerId": "upper", "customerId": "lower" }""");

        Assert.AreEqual("upper", output["a"]?.ToString());
        Assert.AreEqual("lower", output["b"]?.ToString());
    }

    /// <summary>Old: <c>SourceMapping_MissingVariableDoesNotThrow</c>.</summary>
    [TestMethod]
    public void MissingFieldIsNull() =>
        Assert.AreEqual(JTokenType.Null,
            Run(new MappingRules { Fields = [Field("x", Path("nope"))] }, "{}")["x"]?.Type);

    // ── fixed values ────────────────────────────────────────────────────────────

    /// <summary>Old: <c>FixedMapping_HandlesStringNumberBoolAndNullLiterals</c>.</summary>
    [TestMethod]
    public void FixedLiteralsKeepTheirType()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("s", Fixed("WEB")), Field("n", Fixed(42)),
                Field("f", Fixed(9.5)), Field("b", Fixed(true)), Field("z", Fixed(null)),
            ],
        };

        var output = Run(rules, "{}");
        Assert.AreEqual(JTokenType.String, output["s"]?.Type);
        Assert.AreEqual(JTokenType.Integer, output["n"]?.Type);
        Assert.AreEqual(JTokenType.Float, output["f"]?.Type);
        Assert.AreEqual(JTokenType.Boolean, output["b"]?.Type);
        Assert.AreEqual(JTokenType.Null, output["z"]?.Type);
    }

    /// <summary>
    /// Old: <c>FixedMapping_AllowsEmbeddingSourceVariableInsideString</c>. A template could
    /// interpolate; the equivalent here is <c>concat</c>, which is a function rather than a syntax.
    /// </summary>
    [TestMethod]
    public void CombinesAFieldWithFixedText() =>
        Assert.AreEqual("ORD-A1",
            Run(new MappingRules { Fields = [Field("ref", Fixed("ORD-"), transform: Fn("concat", ("with", "A1")))] },
                "{}")["ref"]?.ToString());

    // ── partner and globals ─────────────────────────────────────────────────────

    /// <summary>Old: <c>PartnerMapping_MapsStringValue</c> and <c>GlobalMapping_MapsStringValue</c>.</summary>
    [TestMethod]
    public void PartnerAndGlobalValues()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["regionCode"] = "JO" },
            Globals = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["defaults"] = new Dictionary<string, string> { ["channel"] = "WEB" },
            },
        };
        var rules = new MappingRules
        {
            Fields =
            [
                Field("region", new ValueSource { Kind = ValueSourceKind.Partner, Key = "regionCode" }),
                Field("channel", new ValueSource
                {
                    Kind = ValueSourceKind.Global, SetId = "defaults", Key = "channel",
                }),
            ],
        };

        var output = Run(rules, "{}", context);
        Assert.AreEqual("JO", output["region"]?.ToString());
        Assert.AreEqual("WEB", output["channel"]?.ToString());
    }

    /// <summary>Old: <c>PartnerMapping_MissingKeyReturnsNull</c>, <c>GlobalMapping_MissingKeyReturnsNull</c>.</summary>
    [TestMethod]
    public void MissingPartnerOrGlobalKeyIsNull()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("a", new ValueSource { Kind = ValueSourceKind.Partner, Key = "nope" }),
                Field("b", new ValueSource { Kind = ValueSourceKind.Global, SetId = "nope", Key = "nope" }),
            ],
        };

        var output = Run(rules, "{}");
        Assert.AreEqual(JTokenType.Null, output["a"]?.Type);
        Assert.AreEqual(JTokenType.Null, output["b"]?.Type);
    }

    /// <summary>
    /// Old: <c>PartnerAndGlobalTypedTargets_AreRepresentedAsNullTemplateLiteral</c> - the old
    /// generator gave up and emitted a literal null for a partner value on a number target, because
    /// it could not know at generation time whether the text would parse. Here the value exists when
    /// the conversion runs, so a numeric partner value converts and a non-numeric one is reported.
    /// </summary>
    [TestMethod]
    public void PartnerValueOnANumberTarget_ConvertsInsteadOfGivingUp()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["rate"] = "1.16", ["name"] = "Acme" },
        };

        Assert.AreEqual(1.16m,
            Run(new MappingRules
            {
                Fields = [Field("rate", new ValueSource { Kind = ValueSourceKind.Partner, Key = "rate" },
                    ValueType.Number)],
            }, "{}", context)["rate"]?.Value<decimal>());

        var ex = Assert.ThrowsException<MappingFailedException>(() =>
            Run(new MappingRules
            {
                Fields = [Field("n", new ValueSource { Kind = ValueSourceKind.Partner, Key = "name" },
                    ValueType.Number)],
            }, "{}", context));

        // The rule is named and the failure is reported, which is the parity this test is about.
        // The value itself is no longer quoted: a partner property is configuration, and this
        // message reaches the stored exchange and the preview API.
        Assert.AreEqual("n", ex.Errors.Single().Target);
        StringAssert.Contains(ex.Message, "cannot convert the configured value");
        Assert.IsFalse(ex.Message.Contains("Acme"), ex.Message);
    }

    // ── lookups ─────────────────────────────────────────────────────────────────

    /// <summary>Old: <c>LookupMapping_NullFallback_WhenHit/WhenMiss</c>, <c>CustomFallback</c>.</summary>
    [TestMethod]
    public void LookupHitMissAndFallback()
    {
        var table = new Dictionary<string, object?> { ["NEW"] = "created", ["DONE"] = "closed" };

        Assert.AreEqual("created",
            Run(new MappingRules { Fields = [Field("s", Path("state"), lookup: new LookupRule { Table = table })] },
                """{ "state": "NEW" }""")["s"]?.ToString());

        Assert.AreEqual(JTokenType.Null,
            Run(new MappingRules { Fields = [Field("s", Path("state"), lookup: new LookupRule { Table = table })] },
                """{ "state": "OTHER" }""")["s"]?.Type);

        Assert.AreEqual("unknown",
            Run(new MappingRules
            {
                Fields = [Field("s", Path("state"),
                    lookup: new LookupRule { Table = table, Fallback = "unknown" })],
            }, """{ "state": "OTHER" }""")["s"]?.ToString());
    }

    /// <summary>Old: <c>LookupMapping_RespectsNumberTargetValueType</c> and the boolean case.</summary>
    [TestMethod]
    public void LookupRespectsTheTargetType()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("n", Path("state"), ValueType.Number,
                    lookup: new LookupRule { Table = new Dictionary<string, object?> { ["NEW"] = 1 } }),
                Field("b", Path("state"), ValueType.Boolean,
                    lookup: new LookupRule { Table = new Dictionary<string, object?> { ["NEW"] = true } }),
            ],
        };

        var output = Run(rules, """{ "state": "NEW" }""");
        Assert.AreEqual(1m, output["n"]?.Value<decimal>());
        Assert.AreEqual(true, output["b"]?.Value<bool>());
    }

    // ── type rules ──────────────────────────────────────────────────────────────

    /// <summary>Old: <c>TypeRules_BoolToString_And_BoolToNumber</c>, <c>NumberToBool</c>, etc.</summary>
    [TestMethod]
    public void TypeConversionsMatchTheOldRules()
    {
        var input = """{ "flag": true, "off": false, "one": 1, "zero": 0, "num": 42, "text": "9.5" }""";

        var rules = new MappingRules
        {
            Fields =
            [
                Field("boolAsText", Path("flag"), ValueType.String),
                Field("boolAsNum", Path("flag"), ValueType.Number),
                Field("offAsNum", Path("off"), ValueType.Number),
                Field("oneAsBool", Path("one"), ValueType.Boolean),
                Field("zeroAsBool", Path("zero"), ValueType.Boolean),
                Field("numAsText", Path("num"), ValueType.String),
                Field("textAsNum", Path("text"), ValueType.Number),
            ],
        };

        var output = Run(rules, input);
        Assert.AreEqual("true", output["boolAsText"]?.ToString());
        Assert.AreEqual(1m, output["boolAsNum"]?.Value<decimal>());
        Assert.AreEqual(0m, output["offAsNum"]?.Value<decimal>());
        Assert.AreEqual(true, output["oneAsBool"]?.Value<bool>());
        Assert.AreEqual(false, output["zeroAsBool"]?.Value<bool>());
        Assert.AreEqual("42", output["numAsText"]?.ToString());
        Assert.AreEqual(9.5m, output["textAsNum"]?.Value<decimal>());
    }

    /// <summary>
    /// Old: <c>TypeRules_StringToBool_IsNull</c> and <c>StringToNumber_InvalidIsNull</c>. The old
    /// mapper produced null; this one fails and names the rule. Deliberate - a partner receiving a
    /// document with a silent hole in it is worse than being told the mapping is wrong.
    /// </summary>
    [TestMethod]
    public void ImpossibleConversionsFailInsteadOfProducingNull()
    {
        var ex = Assert.ThrowsException<MappingFailedException>(() =>
            Run(new MappingRules
            {
                Fields =
                [
                    Field("b", Path("text"), ValueType.Boolean),
                    Field("n", Path("text"), ValueType.Number),
                ],
            }, """{ "text": "maybe" }"""));

        Assert.AreEqual(2, ex.Errors.Count);
    }

    // ── transforms ──────────────────────────────────────────────────────────────

    /// <summary>Old: <c>TransformMapping_AppliesMathExpression</c> - a Scriban expression then.</summary>
    [TestMethod]
    public void AppliesArithmetic() =>
        Assert.AreEqual(116m,
            Run(new MappingRules
            {
                Fields = [Field("total", Path("net"), ValueType.Number, Fn("multiply", ("by", 1.16)))],
            }, """{ "net": 100 }""")["total"]?.Value<decimal>());

    // ── arrays ──────────────────────────────────────────────────────────────────

    /// <summary>Old: <c>ObjectArrayMapping_SupportsSourceLookupTransformFixedPartnerGlobal</c>.</summary>
    [TestMethod]
    public void ObjectArrayWithEveryValueSource()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["regionCode"] = "JO" },
            Globals = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["defaults"] = new Dictionary<string, string> { ["channel"] = "WEB" },
            },
        };

        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "lines", Target = ["items"],
                    Fields =
                    [
                        Field("code", Path("sku")),
                        Field("qty", Path("qty"), ValueType.Number),
                        Field("doubled", Path("qty"), ValueType.Number, Fn("multiply", ("by", 2))),
                        Field("kind", Path("type"),
                            lookup: new LookupRule { Table = new Dictionary<string, object?> { ["P"] = "product" } }),
                        Field("channel", Fixed("WEB")),
                        Field("region", new ValueSource { Kind = ValueSourceKind.Partner, Key = "regionCode" }),
                        Field("dflt", new ValueSource
                        {
                            Kind = ValueSourceKind.Global, SetId = "defaults", Key = "channel",
                        }),
                    ],
                },
            ],
        };

        var items = (JArray)Run(rules, """{ "lines": [ { "sku": "A1", "qty": 2, "type": "P" } ] }""", context)["items"]!;

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("A1", items[0]["code"]?.ToString());
        Assert.AreEqual(2m, items[0]["qty"]?.Value<decimal>());
        Assert.AreEqual(4m, items[0]["doubled"]?.Value<decimal>());
        Assert.AreEqual("product", items[0]["kind"]?.ToString());
        Assert.AreEqual("WEB", items[0]["channel"]?.ToString());
        Assert.AreEqual("JO", items[0]["region"]?.ToString());
        Assert.AreEqual("WEB", items[0]["dflt"]?.ToString());
    }

    /// <summary>Old: <c>ObjectArrayMapping_SupportsFilter</c>.</summary>
    [TestMethod]
    public void ObjectArrayWithFilter()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "lines", Target = ["items"],
                    Where = new FilterRule { Field = "qty", Operator = FilterOperator.GreaterThan, Value = 0 },
                    Fields = [Field("code", Path("sku"))],
                },
            ],
        };

        var items = (JArray)Run(rules,
            """{ "lines": [ { "sku": "A1", "qty": 2 }, { "sku": "B7", "qty": 0 } ] }""")["items"]!;

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("A1", items[0]["code"]?.ToString());
    }

    /// <summary>Old: <c>ObjectArrayMapping_HandlesNestedObjectsInsideItems</c>.</summary>
    [TestMethod]
    public void NestedObjectInsideAnItem()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "lines", Target = ["items"],
                    Fields = [new FieldRule { Target = ["product", "code"], From = Path("sku") }],
                },
            ],
        };

        var items = (JArray)Run(rules, """{ "lines": [ { "sku": "A1" } ] }""")["items"]!;

        Assert.AreEqual("A1", items[0]["product"]?["code"]?.ToString());
    }

    /// <summary>Old: <c>EmptyArray_ProducesEmptyOutputArray</c>.</summary>
    [TestMethod]
    public void EmptySourceArrayGivesAnEmptyArray()
    {
        var rules = new MappingRules
        {
            Lists = [new ListRule { Over = "lines", Target = ["items"], Fields = [Field("c", Path("sku"))] }],
        };

        Assert.AreEqual(0, ((JArray)Run(rules, """{ "lines": [] }""")["items"]!).Count);
    }

    /// <summary>Old: <c>PrimitiveArrayMapping_SupportsSourceFixedPartnerGlobal</c>.</summary>
    [TestMethod]
    public void PrimitiveArray()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "lines", Target = ["skus"],
                    Item = new FieldRule { From = Path("sku") },
                },
            ],
        };

        var skus = (JArray)Run(rules, """{ "lines": [ { "sku": "A1" }, { "sku": "B7" } ] }""")["skus"]!;

        CollectionAssert.AreEqual(new[] { "A1", "B7" }, skus.Select(t => t.ToString()).ToArray());
    }

    /// <summary>Old: <c>NestedArrays_ThreeLevels_AreRenderedCorrectly</c>.</summary>
    [TestMethod]
    public void ThreeLevelsOfNesting()
    {
        const string input = """
            { "orders": [ { "id": "O1", "lines": [ { "sku": "A", "tags": [ { "t": "x" }, { "t": "y" } ] } ] } ] }
            """;

        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "orders", Target = ["orders"],
                    Fields = [Field("ref", Path("id"))],
                    Lists =
                    [
                        new ListRule
                        {
                            Over = "lines", Target = ["items"],
                            Fields = [Field("code", Path("sku"))],
                            Lists =
                            [
                                new ListRule
                                {
                                    Over = "tags", Target = ["labels"],
                                    Item = new FieldRule { From = Path("t") },
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var orders = (JArray)Run(rules, input)["orders"]!;
        var labels = (JArray)orders[0]["items"]![0]["labels"]!;

        Assert.AreEqual("O1", orders[0]["ref"]?.ToString());
        Assert.AreEqual("A", orders[0]["items"]![0]["code"]?.ToString());
        CollectionAssert.AreEqual(new[] { "x", "y" }, labels.Select(t => t.ToString()).ToArray());
    }

    // ── root array output ───────────────────────────────────────────────────────

    /// <summary>Old: <c>RootObjectInput_RootArrayOutput_AllFourModes</c>.</summary>
    [TestMethod]
    public void RootArrayOutput()
    {
        var rules = new MappingRules
        {
            Root = new ListRule
            {
                Over = "lines", Target = ["ignored"],
                Fields = [Field("code", Path("sku")), Field("channel", Fixed("WEB"))],
            },
        };

        var output = Run(rules, """{ "lines": [ { "sku": "A1" }, { "sku": "B7" } ] }""");

        Assert.AreEqual(JTokenType.Array, output.Type);
        var array = (JArray)output;
        Assert.AreEqual(2, array.Count);
        Assert.AreEqual("A1", array[0]["code"]?.ToString());
        Assert.AreEqual("WEB", array[1]["channel"]?.ToString());
    }

    /// <summary>Old: <c>RootObjectInput_RootPrimitiveArray_FromSourceField</c>.</summary>
    [TestMethod]
    public void RootPrimitiveArrayOutput()
    {
        var rules = new MappingRules
        {
            Root = new ListRule { Over = "lines", Item = new FieldRule { From = Path("sku") } },
        };

        var output = (JArray)Run(rules, """{ "lines": [ { "sku": "A1" }, { "sku": "B7" } ] }""");

        CollectionAssert.AreEqual(new[] { "A1", "B7" }, output.Select(t => t.ToString()).ToArray());
    }

    /// <summary>Old: <c>RootObjectInput_RootArrayOutput_EmptySourceArray_ProducesEmptyArray</c>.</summary>
    [TestMethod]
    public void RootArrayOutputFromAnEmptySource()
    {
        var rules = new MappingRules
        {
            Root = new ListRule { Over = "lines", Fields = [Field("c", Path("sku"))] },
        };

        Assert.AreEqual(0, ((JArray)Run(rules, """{ "lines": [] }""")).Count);
    }

    /// <summary>
    /// Old: <c>RootArrayInput_IterateAllItems_ViaItemsLoop</c>. A root-array input is walked with an
    /// empty <c>Over</c>, which resolves to the document itself.
    /// </summary>
    [TestMethod]
    public void RootArrayInputToRootArrayOutput()
    {
        var rules = new MappingRules
        {
            Root = new ListRule { Over = "", Fields = [Field("code", Path("sku"))] },
        };

        var output = (JArray)Run(rules, """[ { "sku": "A1" }, { "sku": "B7" } ]""");

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual("B7", output[1]["code"]?.ToString());
    }

    /// <summary>Old: <c>RootArrayInput_AccessFirstItemField_ViaItemsIndex</c> - reading a root array
    /// into an object output, which the old mapper did by hoisting the first element's fields.</summary>
    [TestMethod]
    public void RootArrayInputToObjectOutput()
    {
        var rules = new MappingRules
        {
            Lists = [new ListRule { Over = "", Target = ["items"], Fields = [Field("code", Path("sku"))] }],
        };

        var items = (JArray)Run(rules, """[ { "sku": "A1" } ]""")["items"]!;

        Assert.AreEqual("A1", items[0]["code"]?.ToString());
    }

    // ── escaping ────────────────────────────────────────────────────────────────

    /// <summary>Old: <c>SpecialCharacters_AreEscapedCorrectly</c>.</summary>
    [TestMethod]
    public void SpecialCharactersSurvive()
    {
        const string awkward = "quote \" backslash \\ newline \n tab \t comma, brace }";

        var output = Run(new MappingRules { Fields = [Field("note", Path("note"))] },
            JObject.FromObject(new { note = awkward }).ToString());

        Assert.AreEqual(awkward, output["note"]?.ToString());
    }
}
