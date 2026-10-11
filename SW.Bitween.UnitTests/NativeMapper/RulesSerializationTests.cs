using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// The wire format between the editor and the mapper.
/// </summary>
/// <remarks>
/// Pinned because both sides depend on it and neither can see the other's types. The editor writes
/// what a JavaScript developer would write - camelCase names, lowercase enum values - so these
/// tests exist to make sure that keeps working rather than being an accident of Newtonsoft's
/// defaults.
/// </remarks>
[TestClass]
public class RulesSerializationTests
{
    private static MappingRules Read(string json) =>
        JsonConvert.DeserializeObject<MappingRules>(json)!;

    [TestMethod]
    public void CamelCasePropertyNames_AreAccepted()
    {
        var rules = Read("""
            { "version": 1, "sourceFormat": "json", "targetFormat": "json",
              "fields": [ { "target": ["a"], "from": { "kind": "path", "path": "x" }, "type": "number" } ] }
            """);

        Assert.AreEqual(1, rules.Version);
        Assert.AreEqual("json", rules.SourceFormat);
        Assert.AreEqual("a", rules.Fields[0].Target[0]);
        Assert.AreEqual("x", rules.Fields[0].From.Path);
    }

    /// <summary>Lowercase enum values, which is what the editor writes.</summary>
    [TestMethod]
    public void LowercaseEnumValues_AreAccepted()
    {
        var rules = Read("""
            { "fields": [
                { "target": ["a"], "from": { "kind": "path" }, "type": "number" },
                { "target": ["b"], "from": { "kind": "fixed" }, "type": "string" },
                { "target": ["c"], "from": { "kind": "partner" }, "type": "boolean" },
                { "target": ["d"], "from": { "kind": "global" } } ] }
            """);

        Assert.AreEqual(ValueSourceKind.Path, rules.Fields[0].From.Kind);
        Assert.AreEqual(ValueType.Number, rules.Fields[0].Type);
        Assert.AreEqual(ValueSourceKind.Fixed, rules.Fields[1].From.Kind);
        Assert.AreEqual(ValueType.String, rules.Fields[1].Type);
        Assert.AreEqual(ValueSourceKind.Partner, rules.Fields[2].From.Kind);
        Assert.AreEqual(ValueType.Boolean, rules.Fields[2].Type);
        Assert.AreEqual(ValueSourceKind.Global, rules.Fields[3].From.Kind);
    }

    /// <summary>
    /// Multi-word kinds arrive camelCased, the same shape as <c>greaterThan</c> on a filter.
    /// </summary>
    /// <summary>
    /// The date order rides on the mapping, not on the rule, so it has to survive the wire
    /// like any other part of the document's shape.
    /// </summary>
    [TestMethod]
    public void SourceDateOrder_AsCamelCase()
    {
        Assert.AreEqual(DateOrder.DayFirst, Read("""{ "sourceDateOrder": "dayFirst" }""").SourceDateOrder);
        Assert.AreEqual(DateOrder.MonthFirst, Read("""{ "sourceDateOrder": "monthFirst" }""").SourceDateOrder);
    }

    /// <summary>
    /// Absent means year-first, so every mapping written before this existed keeps reading the
    /// only dates it could ever have read correctly.
    /// </summary>
    [TestMethod]
    public void SourceDateOrder_DefaultsToYearFirst() =>
        Assert.AreEqual(DateOrder.YearFirst, Read("""{ "fields": [] }""").SourceDateOrder);

    [TestMethod]
    public void RootPathKind_AsCamelCase()
    {
        var rules = Read("""
            { "fields": [ { "target": ["a"], "from": { "kind": "rootPath", "path": "order.ref" } } ] }
            """);

        Assert.AreEqual(ValueSourceKind.RootPath, rules.Fields[0].From.Kind);
        Assert.AreEqual("order.ref", rules.Fields[0].From.Path);
    }

    [TestMethod]
    public void FilterOperators_AsLowercaseNames()
    {
        var rules = Read("""
            { "lists": [ { "over": "x", "where": { "field": "q", "operator": "greaterThan", "value": 0 } } ] }
            """);

        Assert.AreEqual(FilterOperator.GreaterThan, rules.Lists[0].Where!.Operator);
    }

    [TestMethod]
    public void TransformArguments_LandInArgs()
    {
        var rules = Read("""
            { "fields": [ { "target": ["a"], "from": { "kind": "fixed" },
                            "transform": { "fn": "multiply", "by": 1.16 } } ] }
            """);

        Assert.AreEqual("multiply", rules.Fields[0].Transform!.Fn);
        Assert.AreEqual(1.16, rules.Fields[0].Transform!.Args["by"].ToObject<double>());
    }

    /// <summary>An absent type means "leave the value as it is", not a default of string.</summary>
    [TestMethod]
    public void AbsentTypeIsNull() =>
        Assert.IsNull(Read("""{ "fields": [ { "target": ["a"], "from": { "kind": "fixed" } } ] }""")
            .Fields[0].Type);

    [TestMethod]
    public void NestedLoopsAndItemRules()
    {
        var rules = Read("""
            { "lists": [ { "over": "orders", "target": ["o"],
                           "lists": [ { "over": "lines", "target": ["l"],
                                        "item": { "from": { "kind": "path", "path": "sku" } } } ] } ] }
            """);

        Assert.AreEqual("lines", rules.Lists[0].Lists[0].Over);
        Assert.AreEqual("sku", rules.Lists[0].Lists[0].Item!.From.Path);
    }

    [TestMethod]
    public void RootLoop_ForRootArrayOutput() =>
        Assert.AreEqual("lines", Read("""{ "root": { "over": "lines" } }""").Root!.Over);

    /// <summary>Empty rules are valid - a mapping being built has none yet.</summary>
    [TestMethod]
    public void EmptyObject_GivesDefaults()
    {
        var rules = Read("{}");

        Assert.AreEqual(MappingRules.CurrentVersion, rules.Version);
        Assert.AreEqual("json", rules.SourceFormat);
        Assert.IsFalse(rules.Fields.Any());
        Assert.IsNull(rules.Root);
    }

    /// <summary>
    /// A round trip through the serialiser keeps the meaning, so the editor can read back what it
    /// saved without a translation step.
    /// </summary>
    [TestMethod]
    public void RoundTripsThroughSerialisation()
    {
        const string json = """
            { "version": 1, "sourceFormat": "json", "targetFormat": "json",
              "fields": [ { "target": ["customer","name"], "from": { "kind": "path", "path": "o.c" },
                            "transform": { "fn": "upper" }, "type": "string" } ],
              "lists": [ { "over": "o.l", "as": "line", "target": ["lines"],
                           "where": { "field": "qty", "operator": "greaterThan", "value": 0 },
                           "fields": [ { "target": ["sku"], "from": { "kind": "path", "path": "sku" } } ] } ] }
            """;

        var once = JsonConvert.SerializeObject(Read(json));
        var twice = JsonConvert.SerializeObject(Read(once));

        Assert.AreEqual(once, twice);
        var rules = Read(once);
        Assert.AreEqual("upper", rules.Fields[0].Transform!.Fn);
        Assert.AreEqual(FilterOperator.GreaterThan, rules.Lists[0].Where!.Operator);
        CollectionAssert.AreEqual(new[] { "customer", "name" }, rules.Fields[0].Target);
    }

    /// <summary>
    /// Fixed entries, and the three states of <c>over</c>. Absent and empty mean different things
    /// - nothing walked, versus the document is the list - so a serialiser that flattened one into
    /// the other would quietly change what a mapping produces.
    /// </summary>
    [TestMethod]
    public void FixedEntries_AndTheThreeStatesOfOver()
    {
        var rules = Read("""
            { "lists": [
                { "target": ["lines"],
                  "fixed": [ { "fields": [ { "target": ["sku"], "from": { "kind": "fixed", "value": "H" } } ] },
                             { "item": { "from": { "kind": "path", "path": "x" } } } ] },
                { "over": "", "target": ["a"] },
                { "over": "order.line", "target": ["b"] } ] }
            """);

        Assert.IsNull(rules.Lists[0].Over, "absent means nothing is walked");
        Assert.AreEqual(2, rules.Lists[0].Fixed.Count);
        Assert.AreEqual("H", rules.Lists[0].Fixed[0].Fields[0].From.Value);
        Assert.AreEqual("x", rules.Lists[0].Fixed[1].Item!.From.Path);

        Assert.AreEqual("", rules.Lists[1].Over, "empty means the document itself");
        Assert.AreEqual("order.line", rules.Lists[2].Over);
    }

    /// <summary>A list with no fixed entries still reads as an empty set rather than null.</summary>
    [TestMethod]
    public void FixedEntries_DefaultToNone()
    {
        var rules = Read("""{ "lists": [ { "over": "x", "target": ["a"] } ] }""");

        Assert.IsNotNull(rules.Lists[0].Fixed);
        Assert.AreEqual(0, rules.Lists[0].Fixed.Count);
    }
}
