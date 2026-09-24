using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// The engine, tested by asserting on the tree it returns rather than on any document text.
/// </summary>
/// <remarks>
/// That is only possible because <c>Map</c> is a pure function that builds its own neutral tree. Had
/// it written into a format's builder as it went, these tests would have to assert on a log of calls
/// instead of a result.
/// </remarks>
[TestClass]
public class DocumentMapperTests
{
    private static readonly JsonFormat Json = new();

    private const string Order = """
        {
          "order": {
            "customer": "Ali",
            "net": 100,
            "state": "NEW",
            "line": [
              { "sku": "A1", "qty": 2 },
              { "sku": "B7", "qty": 0 },
              { "sku": "C2", "qty": 5 }
            ]
          }
        }
        """;

    private static FieldRule Field(string target, ValueSource from, ValueType? type = null,
        TransformRule? transform = null, LookupRule? lookup = null) =>
        new() { Target = [target], From = from, Type = type, Transform = transform, Lookup = lookup };

    private static ValueSource Path(string path) => new() { Kind = ValueSourceKind.Path, Path = path };
    private static ValueSource RootPath(string path) => new() { Kind = ValueSourceKind.RootPath, Path = path };

    private static TransformRule Transform(string fn, params (string Name, object Value)[] args)
    {
        var rule = new TransformRule { Fn = fn };
        foreach (var (name, value) in args) rule.Args[name] = JToken.FromObject(value);
        return rule;
    }
    private static ValueSource Fixed(object? value) => new() { Kind = ValueSourceKind.Fixed, Value = value };

    private static ObjectNode Map(MappingRules rules, string document, MappingContext? context = null) =>
        (ObjectNode)DocumentMapper.Map(rules, Json.Read(document), context ?? MappingContext.Empty);

    private static ValueNode MapAny(MappingRules rules, string document) =>
        DocumentMapper.Map(rules, Json.Read(document), MappingContext.Empty);

    private static object? Scalar(ObjectNode output, string path) => Values.ResolveScalar(output, path);

    // ── the four value sources ──────────────────────────────────────────────────

    [TestMethod]
    public void PathSource_ReadsTheDocument()
    {
        var output = Map(new MappingRules { Fields = [Field("customerName", Path("order.customer"))] }, Order);

        Assert.AreEqual("Ali", Scalar(output, "customerName"));
    }

    [TestMethod]
    public void FixedSource_UsesTheLiteral()
    {
        var output = Map(new MappingRules { Fields = [Field("channel", Fixed("WEB"))] }, Order);

        Assert.AreEqual("WEB", Scalar(output, "channel"));
    }

    /// <summary>
    /// Partner values come from the context, never from the payload — that is the whole point of the
    /// new mapper being handed its context instead of having it written into the document.
    /// </summary>
    [TestMethod]
    public void PartnerSource_ReadsTheContext()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["region-code"] = "JO" },
        };
        var rules = new MappingRules
        {
            Fields = [Field("region", new ValueSource { Kind = ValueSourceKind.Partner, Key = "region-code" })],
        };

        Assert.AreEqual("JO", Scalar(Map(rules, Order, context), "region"));
    }

    /// <summary>
    /// A hyphenated key works. In the old mapper this exact case silently produced null inside a
    /// fixed array item, because the key was spliced into a Scriban expression where the hyphen
    /// parsed as subtraction.
    /// </summary>
    [TestMethod]
    public void PartnerSource_HandlesAwkwardKeys()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string>
            {
                ["api-key"] = "abc",
                ["2fa enabled"] = "yes",
            },
        };
        var rules = new MappingRules
        {
            Fields =
            [
                Field("k", new ValueSource { Kind = ValueSourceKind.Partner, Key = "api-key" }),
                Field("f", new ValueSource { Kind = ValueSourceKind.Partner, Key = "2fa enabled" }),
            ],
        };

        var output = Map(rules, Order, context);
        Assert.AreEqual("abc", Scalar(output, "k"));
        Assert.AreEqual("yes", Scalar(output, "f"));
    }

    [TestMethod]
    public void GlobalSource_ReadsTheContext()
    {
        var context = new MappingContext
        {
            Globals = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["fx"] = new Dictionary<string, string> { ["vat"] = "1.16" },
            },
        };
        var rules = new MappingRules
        {
            Fields =
            [
                Field("vat", new ValueSource { Kind = ValueSourceKind.Global, SetId = "fx", Key = "vat" },
                    ValueType.Number),
            ],
        };

        Assert.AreEqual(1.16m, Scalar(Map(rules, Order, context), "vat"));
    }

    [TestMethod]
    public void MissingPartnerOrGlobal_IsNull()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("a", new ValueSource { Kind = ValueSourceKind.Partner, Key = "nope" }),
                Field("b", new ValueSource { Kind = ValueSourceKind.Global, SetId = "nope", Key = "nope" }),
            ],
        };

        var output = Map(rules, Order);
        Assert.IsNull(Scalar(output, "a"));
        Assert.IsNull(Scalar(output, "b"));
    }

    // ── absence ─────────────────────────────────────────────────────────────────

    /// <summary>An optional field that is not there is null, not a failure.</summary>
    [TestMethod]
    public void MissingPath_IsNullNotAnError() =>
        Assert.IsNull(Scalar(Map(new MappingRules { Fields = [Field("x", Path("order.nope"))] }, Order), "x"));

    // ── nesting and order ───────────────────────────────────────────────────────

    [TestMethod]
    public void NestedTarget_BuildsTheObjects()
    {
        var rules = new MappingRules
        {
            Fields = [new FieldRule { Target = ["customer", "name"], From = Path("order.customer") }],
        };

        Assert.AreEqual("Ali", Scalar(Map(rules, Order), "customer.name"));
    }

    /// <summary>
    /// Output order is the order of the rules, not of the source document and not of any sample —
    /// which is what makes it deterministic and what XML sequences require.
    /// </summary>
    [TestMethod]
    public void OutputOrder_FollowsTheRules()
    {
        var rules = new MappingRules
        {
            Fields = [Field("zebra", Fixed(1)), Field("apple", Fixed(2)), Field("mango", Fixed(3))],
        };

        CollectionAssert.AreEqual(new[] { "zebra", "apple", "mango" }, Map(rules, Order).Keys.ToArray());
    }

    // ── transforms, lookups, types ──────────────────────────────────────────────

    [TestMethod]
    public void Transform_ThenType_AreBothApplied()
    {
        var transform = new TransformRule { Fn = "multiply" };
        transform.Args["by"] = JToken.FromObject(1.16);

        var rules = new MappingRules
        {
            Fields = [Field("total", Path("order.net"), ValueType.Number, transform)],
        };

        // The reason the mapper uses decimal: in double this is 115.99999999999999.
        Assert.AreEqual(116m, Scalar(Map(rules, Order), "total"));
    }

    [TestMethod]
    public void Lookup_SubstitutesAValue()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("state", Path("order.state"), ValueType.Number,
                    lookup: new LookupRule { Table = new Dictionary<string, object?> { ["NEW"] = 1, ["DONE"] = 2 } }),
            ],
        };

        Assert.AreEqual(1m, Scalar(Map(rules, Order), "state"));
    }

    [TestMethod]
    public void Lookup_Miss_UsesTheFallback()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("state", Path("order.state"),
                    lookup: new LookupRule
                    {
                        Table = new Dictionary<string, object?> { ["OTHER"] = "x" },
                        Fallback = "UNKNOWN",
                    }),
            ],
        };

        Assert.AreEqual("UNKNOWN", Scalar(Map(rules, Order), "state"));
    }

    [TestMethod]
    public void Lookup_MissWithNoFallback_IsNull()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("state", Path("order.state"),
                    lookup: new LookupRule { Table = new Dictionary<string, object?> { ["OTHER"] = "x" } }),
            ],
        };

        Assert.IsNull(Scalar(Map(rules, Order), "state"));
    }

    /// <summary>
    /// The table is asked about the value after the transform, so a table keyed on "JO" matches a
    /// document that says "jo" once it has been uppercased. The other order would miss.
    /// </summary>
    [TestMethod]
    public void Lookup_IsAskedAboutTheTransformedValue()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("countryName", Path("country"), transform: Transform("upper"),
                    lookup: new LookupRule { Table = new Dictionary<string, object?> { ["JO"] = "Jordan" } }),
            ],
        };

        Assert.AreEqual("Jordan", Scalar(Map(rules, """{ "country": "jo" }"""), "countryName"));
    }

    /// <summary>
    /// How an ambiguous date is read is a setting on the mapping, not on the rule — a partner writes
    /// dates one way throughout. The transforms have their own tests; this pins that the mapping's
    /// setting is what reaches them.
    /// </summary>
    [TestMethod]
    public void SourceDateOrder_DecidesHowTheTransformsReadADate()
    {
        const string document = """{ "shippingDate": "04.09.2026" }""";
        MappingRules Rules(DateOrder order) => new()
        {
            SourceDateOrder = order,
            Fields = [Field("shipDate", Path("shippingDate"), transform: Transform("formatDate", ("format", "yyyy-MM-dd")))],
        };

        Assert.AreEqual("2026-09-04", Scalar(Map(Rules(DateOrder.DayFirst), document), "shipDate"));
        Assert.AreEqual("2026-04-09", Scalar(Map(Rules(DateOrder.MonthFirst), document), "shipDate"));

        // Left at year-first, it is refused rather than guessed.
        var refused = Assert.ThrowsException<MappingFailedException>(() => Map(Rules(DateOrder.YearFirst), document));
        StringAssert.Contains(refused.Errors[0].Reason, "04.09.2026");
    }

    // ── loops ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Loop_ProducesOneRowPerItem()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", As = "line", Target = ["lines"],
                    Fields = [Field("code", Path("sku"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;
        Assert.AreEqual(3, lines.Items.Count);
        CollectionAssert.AreEqual(
            new[] { "A1", "B7", "C2" },
            lines.Items.Select(i => Values.ResolveScalar((ObjectNode)i, "code")).ToArray());
    }

    [TestMethod]
    public void Loop_Filter_SkipsItems()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", As = "line", Target = ["lines"],
                    Where = new FilterRule { Field = "qty", Operator = FilterOperator.GreaterThan, Value = 0 },
                    Fields = [Field("code", Path("sku"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;
        CollectionAssert.AreEqual(
            new[] { "A1", "C2" },
            lines.Items.Select(i => Values.ResolveScalar((ObjectNode)i, "code")).ToArray());
    }

    [TestMethod]
    public void Loop_EveryFilterOperator()
    {
        var cases = new (FilterOperator Op, object Value, int Expected)[]
        {
            (FilterOperator.Equal, 2, 1),
            (FilterOperator.NotEqual, 2, 2),
            (FilterOperator.GreaterThan, 0, 2),
            (FilterOperator.GreaterThanOrEqual, 2, 2),
            (FilterOperator.LessThan, 5, 2),
            (FilterOperator.LessThanOrEqual, 2, 2),
        };

        foreach (var (op, value, expected) in cases)
        {
            var rules = new MappingRules
            {
                Lists =
                [
                    new ListRule
                    {
                        Over = "order.line", Target = ["lines"],
                        Where = new FilterRule { Field = "qty", Operator = op, Value = value },
                        Fields = [Field("code", Path("sku"))],
                    },
                ],
            };

            var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;
            Assert.AreEqual(expected, lines.Items.Count, $"{op} {value}");
        }
    }

    /// <summary>An order with no lines is ordinary, so an absent list is an empty list.</summary>
    [TestMethod]
    public void Loop_OverAMissingPath_IsAnEmptyList()
    {
        var rules = new MappingRules
        {
            Lists = [new ListRule { Over = "order.nope", Target = ["lines"], Fields = [Field("c", Path("sku"))] }],
        };

        Assert.AreEqual(0, ((ListNode)Values.Resolve(Map(rules, Order), "lines")!).Items.Count);
    }

    [TestMethod]
    public void Loop_ItemFieldsAndRootFieldsTogether()
    {
        var rules = new MappingRules
        {
            Fields = [Field("customerName", Path("order.customer"))],
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fields = [Field("code", Path("sku")), Field("channel", Fixed("WEB"))],
                },
            ],
        };

        var output = Map(rules, Order);
        Assert.AreEqual("Ali", Scalar(output, "customerName"));
        var first = ((ListNode)Values.Resolve(output, "lines")!).Items[0];
        Assert.AreEqual("A1", Values.ResolveScalar(first, "code"));
        Assert.AreEqual("WEB", Values.ResolveScalar(first, "channel"));
    }

    [TestMethod]
    public void NestedLoops_WalkTheInnerList()
    {
        const string document = """
            { "orders": [
                { "id": "O1", "lines": [ { "sku": "A" }, { "sku": "B" } ] },
                { "id": "O2", "lines": [ { "sku": "C" } ] } ] }
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
                        new ListRule { Over = "lines", Target = ["items"], Fields = [Field("code", Path("sku"))] },
                    ],
                },
            ],
        };

        var orders = (ListNode)Values.Resolve(Map(rules, document), "orders")!;
        Assert.AreEqual(2, orders.Items.Count);
        Assert.AreEqual("O1", Values.ResolveScalar(orders.Items[0], "ref"));
        Assert.AreEqual(2, ((ListNode)Values.Resolve(orders.Items[0], "items")!).Items.Count);
        Assert.AreEqual(1, ((ListNode)Values.Resolve(orders.Items[1], "items")!).Items.Count);
    }

    /// <summary>
    /// A list of plain values produces one value per entry, and that value is a whole rule: it is
    /// transformed and typed exactly like a named field would be.
    /// </summary>
    [TestMethod]
    public void AListOfPlainValues_IsTransformedAndTypedLikeAnyField()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "price", Target = ["totals"],
                    Item = Field("", Path(""), ValueType.Number, Transform("multiply", ("by", 2))),
                },
            ],
        };

        var totals = (ListNode)Values.Resolve(Map(rules, """{ "price": [10, 20] }"""), "totals")!;

        CollectionAssert.AreEqual(
            new object?[] { 20m, 40m },
            totals.Items.Select(i => ((ScalarNode)i).Value).ToArray());
    }

    // ── failure ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The behaviour that was chosen deliberately: fail the exchange rather than write a document
    /// with a hole in it, and name every rule so it takes one run to fix them all.
    /// </summary>
    [TestMethod]
    public void EveryFailingRuleIsReported()
    {
        var rules = new MappingRules
        {
            Fields =
            [
                Field("good", Path("order.customer")),
                Field("bad1", Path("order.customer"), ValueType.Number),
                Field("bad2", Path("order.state"), ValueType.Boolean),
            ],
        };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        Assert.AreEqual(2, ex.Errors.Count);
        CollectionAssert.AreEquivalent(new[] { "bad1", "bad2" }, ex.Errors.Select(e => e.Target).ToArray());
        StringAssert.Contains(ex.Message, "2 rules");
        StringAssert.Contains(ex.Message, "bad1");
        StringAssert.Contains(ex.Message, "bad2");
    }

    [TestMethod]
    public void OneFailingRule_ReadsAsOne()
    {
        var rules = new MappingRules { Fields = [Field("bad", Path("order.customer"), ValueType.Number)] };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        StringAssert.Contains(ex.Message, "1 rule");
        StringAssert.Contains(ex.Errors[0].Reason, "cannot convert 'Ali' to number");
    }

    /// <summary>A failing rule inside a loop names the loop it is in.</summary>
    [TestMethod]
    public void FailureInsideALoop_NamesItsPath()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fields = [Field("code", Path("sku"), ValueType.Number)],
                },
            ],
        };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        StringAssert.Contains(ex.Errors[0].Target, "lines");
        StringAssert.Contains(ex.Errors[0].Target, "code");
    }

    [TestMethod]
    public void RuleWithNoTarget_IsReported()
    {
        var rules = new MappingRules { Fields = [new FieldRule { From = Fixed("x") }] };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        StringAssert.Contains(ex.Errors[0].Reason, "no target");
    }

    [TestMethod]
    public void EmptyRules_ProduceAnEmptyDocument() =>
        Assert.AreEqual(0, Map(new MappingRules(), Order).Keys.Count);

    // ── reading from the top of the document ────────────────────────────────────

    /// <summary>
    /// The reason this kind exists: one value from the order written onto every line.
    /// A plain path inside a list can only see the line it is on.
    /// </summary>
    [TestMethod]
    public void RootPath_InsideAList_ReadsTheDocumentNotTheEntry()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fields = [Field("code", Path("sku")), Field("customer", RootPath("order.customer"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;

        CollectionAssert.AreEqual(
            new[] { "A1", "B7", "C2" },
            lines.Items.Select(i => Scalar((ObjectNode)i, "code")).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Ali", "Ali", "Ali" },
            lines.Items.Select(i => Scalar((ObjectNode)i, "customer")).ToArray());
    }

    /// <summary>A plain path inside a list still reads the entry, which is the common case.</summary>
    [TestMethod]
    public void Path_InsideAList_StillReadsTheEntry()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    // `order.customer` means nothing inside a line, and resolves to nothing.
                    Fields = [Field("customer", Path("order.customer"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;

        Assert.IsTrue(lines.Items.All(i => Scalar((ObjectNode)i, "customer") is null));
    }

    /// <summary>At the top level there is no entry to be inside, so the two are the same thing.</summary>
    [TestMethod]
    public void RootPath_AtTheTopLevel_IsTheSameAsAPath()
    {
        var rules = new MappingRules
        {
            Fields = [Field("a", Path("order.customer")), Field("b", RootPath("order.customer"))],
        };

        var output = Map(rules, Order);

        Assert.AreEqual("Ali", Scalar(output, "a"));
        Assert.AreEqual("Ali", Scalar(output, "b"));
    }

    /// <summary>The document stays reachable however deep the lists go.</summary>
    [TestMethod]
    public void RootPath_InsideANestedList_StillReachesTheDocument()
    {
        const string document = """
            { "ref": "SO-1", "orders": [ { "lines": [ { "sku": "A" }, { "sku": "B" } ] } ] }
            """;

        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "orders", Target = ["orders"],
                    Lists =
                    [
                        new ListRule
                        {
                            Over = "lines", Target = ["items"],
                            Fields = [Field("code", Path("sku")), Field("ref", RootPath("ref"))],
                        },
                    ],
                },
            ],
        };

        var items = (ListNode)Values.Resolve(
            ((ListNode)Values.Resolve(Map(rules, document), "orders")!).Items[0], "items")!;

        CollectionAssert.AreEqual(
            new[] { "SO-1", "SO-1" },
            items.Items.Select(i => Scalar((ObjectNode)i, "ref")).ToArray());
    }

    [TestMethod]
    public void RootPath_ToAMissingField_IsEmptyRatherThanAnError()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fields = [Field("nope", RootPath("order.nothing"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;

        Assert.IsTrue(lines.Items.All(i => Scalar((ObjectNode)i, "nope") is null));
    }

    /// <summary>A value read from the document is still transformed and cast like any other.</summary>
    [TestMethod]
    public void RootPath_IsTransformedAndCastLikeAnyOtherSource()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fields =
                    [
                        Field("total", RootPath("order.net"), ValueType.Number,
                            Transform("multiply", ("by", 1.16m))),
                    ],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;

        Assert.IsTrue(lines.Items.All(i => Equals(Scalar((ObjectNode)i, "total"), 116m)));
    }

    // ── entries no source list produced ─────────────────────────────────────────

    private static ListEntry Entry(params FieldRule[] fields) =>
        new() { Fields = fields.ToList() };

    /// <summary>
    /// A header line a partner expects, in front of the ones the source produced. Same order the
    /// previous mapper emitted for the same configuration.
    /// </summary>
    [TestMethod]
    public void FixedEntries_ComeBeforeTheWalkedOnes()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fixed = [Entry(Field("sku", Fixed("HEADER")))],
                    Fields = [Field("sku", Path("sku"))],
                },
            ],
        };

        var lines = (ListNode)Values.Resolve(Map(rules, Order), "lines")!;

        CollectionAssert.AreEqual(
            new[] { "HEADER", "A1", "B7", "C2" },
            lines.Items.Select(i => Scalar((ObjectNode)i, "sku")).ToArray());
    }

    /// <summary>
    /// A fixed entry has no entry of its own, so its paths read the scope the list sits in —
    /// which is what lets one carry a value from the document.
    /// </summary>
    [TestMethod]
    public void AFixedEntry_ReadsTheScopeTheListSitsIn()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fixed = [Entry(Field("sku", Fixed("HEADER")), Field("who", Path("order.customer")))],
                    Fields = [Field("sku", Path("sku"))],
                },
            ],
        };

        var first = (ObjectNode)((ListNode)Values.Resolve(Map(rules, Order), "lines")!).Items[0];

        Assert.AreEqual("Ali", Scalar(first, "who"));
    }

    /// <summary>A fixed entry can carry a partner value too, like the header line a partner expects.</summary>
    [TestMethod]
    public void AFixedEntry_ReadsThePartner()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["WarehouseCode"] = "WH-7" },
        };
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = "order.line", Target = ["lines"],
                    Fixed =
                    [
                        Entry(Field("sku", Fixed("HEADER")),
                            Field("warehouse", new ValueSource { Kind = ValueSourceKind.Partner, Key = "WarehouseCode" })),
                    ],
                    Fields = [Field("sku", Path("sku"))],
                },
            ],
        };

        var first = (ObjectNode)((ListNode)Values.Resolve(Map(rules, Order, context), "lines")!).Items[0];

        Assert.AreEqual("WH-7", Scalar(first, "warehouse"));
    }

    /// <summary>
    /// No source list to walk: the list is exactly its fixed entries. This is what the previous
    /// mapper called a primitive array — one slot per rule rather than one per source entry.
    /// </summary>
    [TestMethod]
    public void NoSourceList_GivesExactlyTheFixedEntries()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = null, Target = ["codes"],
                    Fixed =
                    [
                        new ListEntry { Item = Field("", Path("order.customer")) },
                        new ListEntry { Item = Field("", Fixed("X")) },
                    ],
                },
            ],
        };

        var codes = (ListNode)Values.Resolve(Map(rules, Order), "codes")!;

        CollectionAssert.AreEqual(
            new object?[] { "Ali", "X" },
            codes.Items.Select(i => ((ScalarNode)i).Value).ToArray());
    }

    /// <summary>
    /// The difference between "nothing to walk" and "the document is the list". An absent path is
    /// not the same as an empty one, and confusing the two would silently change what is produced.
    /// </summary>
    [TestMethod]
    public void AnAbsentSourcePath_IsNotTheSameAsAnEmptyOne()
    {
        const string document = """[ { "sku": "A" }, { "sku": "B" } ]""";

        var walksTheDocument = new MappingRules
        {
            Root = new ListRule { Over = "", Fields = [Field("code", Path("sku"))] },
        };
        var walksNothing = new MappingRules
        {
            Root = new ListRule { Over = null, Fields = [Field("code", Path("sku"))] },
        };

        Assert.AreEqual(2, ((ListNode)MapAny(walksTheDocument, document)).Items.Count);
        Assert.AreEqual(0, ((ListNode)MapAny(walksNothing, document)).Items.Count);
    }

    /// <summary>A fixed entry may hold a list of its own, which the old literal JSON could not.</summary>
    [TestMethod]
    public void AFixedEntry_MayHoldItsOwnList()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = null, Target = ["lines"],
                    Fixed =
                    [
                        new ListEntry
                        {
                            Fields = [Field("sku", Fixed("HEADER"))],
                            Lists =
                            [
                                new ListRule
                                {
                                    Over = "order.line", Target = ["all"],
                                    Item = Field("", Path("sku")),
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var first = (ObjectNode)((ListNode)Values.Resolve(Map(rules, Order), "lines")!).Items[0];
        var all = (ListNode)Values.Resolve(first, "all")!;

        CollectionAssert.AreEqual(
            new object?[] { "A1", "B7", "C2" },
            all.Items.Select(i => ((ScalarNode)i).Value).ToArray());
    }

    /// <summary>A rule that fails inside a fixed entry is reported like any other.</summary>
    [TestMethod]
    public void AFailingRuleInAFixedEntry_IsReported()
    {
        var rules = new MappingRules
        {
            Lists =
            [
                new ListRule
                {
                    Over = null, Target = ["lines"],
                    Fixed = [Entry(Field("qty", Fixed("not a number"), ValueType.Number))],
                },
            ],
        };

        var error = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        Assert.IsTrue(error.Errors.Any(e => e.Reason.Contains("not a number")), error.Message);
    }

    // ─── Rules the mapping cannot honour ─────────────────────────────────────

    [TestMethod]
    public void RootList_AlongsideTopLevelRules_IsReported()
    {
        // A document is a list or an object. Rules for the shape it is not will never take
        // effect, and producing a document that quietly lacks them is the worst of both.
        var rules = new MappingRules
        {
            Root = new ListRule { Over = "order.line", Fields = [Field("code", Path("sku"))] },
            Fields = [Field("customer", Path("order.customer"))],
        };

        var ex = Assert.ThrowsException<MappingFailedException>(() => MapAny(rules, Order));

        Assert.IsTrue(ex.Errors.Any(e => e.Target == "(root)"), ex.Message);
        StringAssert.Contains(ex.Message, "the whole output is a list");
    }

    [TestMethod]
    public void RootList_OnItsOwn_IsNotReported()
    {
        var rules = new MappingRules
        {
            Root = new ListRule { Over = "order.line", Fields = [Field("code", Path("sku"))] },
        };

        Assert.IsInstanceOfType<ListNode>(MapAny(rules, Order));
    }

    [TestMethod]
    public void ACoercionError_QuotesADocumentValue()
    {
        var rules = new MappingRules { Fields = [Field("n", Path("order.customer"), ValueType.Number)] };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order));

        // Worth quoting: it came out of the document being mapped, and seeing it is most of
        // what makes the message useful.
        StringAssert.Contains(ex.Message, "'Ali'");
    }

    [TestMethod]
    public void ACoercionError_DoesNotQuoteAPartnerOrGlobalValue()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["password"] = "s3cr3t-value" },
            Globals = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["set"] = new Dictionary<string, string> { ["key"] = "also-secret" },
            },
        };
        var rules = new MappingRules
        {
            Fields =
            [
                Field("a", new ValueSource { Kind = ValueSourceKind.Partner, Key = "password" },
                    ValueType.Number),
                Field("b", new ValueSource { Kind = ValueSourceKind.Global, SetId = "set", Key = "key" },
                    ValueType.Number),
            ],
        };

        var ex = Assert.ThrowsException<MappingFailedException>(() => Map(rules, Order, context));

        // The message is stored on the exchange and returned by the preview API, and an adapter
        // password is configuration rather than payload — so its length is said, not its value.
        Assert.IsFalse(ex.Message.Contains("s3cr3t-value"), ex.Message);
        Assert.IsFalse(ex.Message.Contains("also-secret"), ex.Message);
        StringAssert.Contains(ex.Message, "12 characters");
        StringAssert.Contains(ex.Message, "11 characters");
    }
}
