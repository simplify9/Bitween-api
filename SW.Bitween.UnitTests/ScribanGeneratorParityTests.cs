using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Parity tests: each template string here is the EXACT output of scribanGenerator.ts
/// for the corresponding mapping config (frozen in scribanGeneratorOutput.test.ts snapshots).
///
/// If a test here fails it means ScribanJsonHelper.Render cannot execute a template
/// that the UI generator produces — a production bug.
/// If the TypeScript snapshot changes, the template constant here must be updated to match.
/// </summary>
[TestClass]
public class ScribanGeneratorParityTests
{
    // ── Source field rename ────────────────────────────────────────────────────
    // Config: { target: 'orderId', source: 'Order.Id' }

    [TestMethod]
    public void Parity_SourceFieldRename_RendersCorrectly()
    {
        const string template = """
            {
              "orderId": {{ Order.Id | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"Order": {"Id": "ORD-001"}}""");
        Assert.AreEqual("ORD-001", result["orderId"]?.ToString());
    }

    // ── Fixed string value ─────────────────────────────────────────────────────
    // Config: { target: 'status', source: '', fixedValue: 'active' }

    [TestMethod]
    public void Parity_FixedStringValue_RendersCorrectly()
    {
        const string template = """
            {
              "status": "active",
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, "{}");
        Assert.AreEqual("active", result["status"]?.ToString());
    }

    // ── Fixed number value ─────────────────────────────────────────────────────
    // Config: { target: 'count', source: '', fixedValue: '42' }

    [TestMethod]
    public void Parity_FixedNumberValue_RendersCorrectly()
    {
        const string template = """
            {
              "count": 42,
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, "{}");
        Assert.AreEqual(42, result["count"]?.Value<int>());
    }

    // ── Fixed boolean value ────────────────────────────────────────────────────
    // Config: { target: 'flag', source: '', fixedValue: 'true' }

    [TestMethod]
    public void Parity_FixedBooleanValue_RendersCorrectly()
    {
        const string template = """
            {
              "flag": true,
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, "{}");
        Assert.AreEqual(true, result["flag"]?.Value<bool>());
    }

    // ── Partner property ───────────────────────────────────────────────────────
    // Config: { target: 'pkey', source: '', partnerPropKey: 'apiKey' }

    [TestMethod]
    public void Parity_PartnerProperty_RendersCorrectly()
    {
        const string template = """
            {
              "pkey": {{ __partner__?.apiKey | json }},
            }
            """;
        const string inputJson = """{"__partner__": {"apiKey": "secret-123"}}""";
        var result = ScribanJsonTestHelper.RenderObject(template, inputJson);
        Assert.AreEqual("secret-123", result["pkey"]?.ToString());
    }

    [TestMethod]
    public void Parity_PartnerProperty_MissingPartner_ReturnsNull()
    {
        const string template = """
            {
              "pkey": {{ __partner__?.apiKey | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, "{}");
        Assert.AreEqual(JTokenType.Null, result["pkey"]?.Type);
    }

    // ── Global set key ─────────────────────────────────────────────────────────
    // Config: { target: 'gval', source: '', globalSetId: 'mySet', globalKey: 'region' }

    [TestMethod]
    public void Parity_GlobalSetKey_RendersCorrectly()
    {
        const string template = """
            {
              "gval": {{ __globals__?.mySet["region"] | json }},
            }
            """;
        const string inputJson = """{"__globals__": {"mySet": {"region": "eu-west"}}}""";
        var result = ScribanJsonTestHelper.RenderObject(template, inputJson);
        Assert.AreEqual("eu-west", result["gval"]?.ToString());
    }

    // ── Lookup — null fallback ─────────────────────────────────────────────────
    // Config: { target: 'category', source: 'Cat', lookupDictionary: { entries:[{A→Alpha}], fallback:'null' } }

    [TestMethod]
    public void Parity_LookupNullFallback_HitReturnsValue()
    {
        const string template = """
            {
              "category": {{ $__e = { "A": "Alpha" }; $__e[Cat] | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"Cat": "A"}""");
        Assert.AreEqual("Alpha", result["category"]?.ToString());
    }

    [TestMethod]
    public void Parity_LookupNullFallback_MissReturnsNull()
    {
        const string template = """
            {
              "category": {{ $__e = { "A": "Alpha" }; $__e[Cat] | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"Cat": "Z"}""");
        Assert.AreEqual(JTokenType.Null, result["category"]?.Type);
    }

    // ── Lookup — custom fallback ───────────────────────────────────────────────
    // Config: { fallback:'custom', fallbackValue:'Unknown' }

    [TestMethod]
    public void Parity_LookupCustomFallback_MissReturnsCustomValue()
    {
        const string template = """
            {
              "category": {{ $__e = { "A": "Alpha" }; ($__e[Cat] ?? "Unknown") | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"Cat": "Z"}""");
        Assert.AreEqual("Unknown", result["category"]?.ToString());
    }

    // ── Transform ──────────────────────────────────────────────────────────────
    // Config: { target: 'doubled', source: 'amount', transform: 'value * 2' }

    [TestMethod]
    public void Parity_Transform_ArithmeticExpression_RendersCorrectly()
    {
        const string template = """
            {
              "doubled": {{ amount * 2 | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"amount": 5}""");
        Assert.AreEqual(10, result["doubled"]?.Value<int>());
    }

    // ── Object array mapping ───────────────────────────────────────────────────
    // Config: source:Items → target:lines, alias:item, mappings:[sku←Sku, qty←Quantity]

    [TestMethod]
    public void Parity_ObjectArrayMapping_RendersAllItems()
    {
        const string template = """
            {
              "lines": [
              {{- for item in Items -}}
              {
                "sku": {{ item.Sku | json }},
                "qty": {{ item.Quantity | json }},
              },
              {{- end -}}
              ],
            }
            """;
        const string inputJson = """
            {
              "Items": [
                {"Sku": "AAA", "Quantity": 2},
                {"Sku": "BBB", "Quantity": 5}
              ]
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, inputJson);
        var lines = result["lines"] as JArray;
        Assert.IsNotNull(lines);
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("AAA", lines[0]["sku"]?.ToString());
        Assert.AreEqual(5, lines[1]["qty"]?.Value<int>());
    }

    // ── Type rules ────────────────────────────────────────────────────────────
    // Each template string is the EXACT output of generateScriban when outputJson
    // and inputJson are provided and their types differ, triggering castExpr.

    [TestMethod]
    public void Parity_TypeRule_BoolToString_EmitsCorrectCast()
    {
        // generateScriban: outputJson={"result":""}, inputJson={"flag":true}
        // → castExpr('flag','string','boolean') = (flag == null ? null : (flag ? "true" : "false"))
        const string template = """
            {
              "result": {{ (flag == null ? null : (flag ? "true" : "false")) | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"flag": true}""");
        Assert.AreEqual("true", result["result"]?.ToString());

        var result2 = ScribanJsonTestHelper.RenderObject(template, """{"flag": false}""");
        Assert.AreEqual("false", result2["result"]?.ToString());
    }

    [TestMethod]
    public void Parity_TypeRule_NumberToString_EmitsCorrectCast()
    {
        // generateScriban: outputJson={"result":""}, inputJson={"count":42}
        // → castExpr('count','string','number') = ("" + count)
        const string template = """
            {
              "result": {{ ("" + count) | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"count": 42}""");
        Assert.AreEqual(JTokenType.String, result["result"]?.Type);
        Assert.AreEqual("42", result["result"]?.ToString());
    }

    [TestMethod]
    public void Parity_TypeRule_StringToNumber_UsesToFloat()
    {
        // generateScriban: outputJson={"result":0}, inputJson={"price":"9.99"}
        // → castExpr('price','number','string') = (price | to_float)
        const string template = """
            {
              "result": {{ (price | to_float) | json }},
            }
            """;
        var hit = ScribanJsonTestHelper.RenderObject(template, """{"price": "9.99"}""");
        Assert.AreEqual(JTokenType.Float, hit["result"]?.Type);
        Assert.IsTrue(Math.Abs((hit["result"]?.Value<double>() ?? 0) - 9.99) < 0.0001);

        // invalid string → null
        var miss = ScribanJsonTestHelper.RenderObject(template, """{"price": "abc"}""");
        Assert.AreEqual(JTokenType.Null, miss["result"]?.Type);
    }

    [TestMethod]
    public void Parity_TypeRule_BoolToNumber_EmitsCorrectCast()
    {
        // generateScriban: outputJson={"result":0}, inputJson={"flag":true}
        // → castExpr('flag','number','boolean') = (flag == null ? null : (flag ? 1 : 0))
        const string template = """
            {
              "result": {{ (flag == null ? null : (flag ? 1 : 0)) | json }},
            }
            """;
        var trueResult = ScribanJsonTestHelper.RenderObject(template, """{"flag": true}""");
        Assert.AreEqual(1, trueResult["result"]?.Value<int>());

        var falseResult = ScribanJsonTestHelper.RenderObject(template, """{"flag": false}""");
        Assert.AreEqual(0, falseResult["result"]?.Value<int>());
    }

    [TestMethod]
    public void Parity_TypeRule_NumberToBool_EmitsCorrectCast()
    {
        // generateScriban: outputJson={"result":false}, inputJson={"amount":1}
        // → castExpr('amount','boolean','number') = (amount == 0 ? false : (amount == 1 ? true : null))
        const string template = """
            {
              "result": {{ (amount == 0 ? false : (amount == 1 ? true : null)) | json }},
            }
            """;
        var zero = ScribanJsonTestHelper.RenderObject(template, """{"amount": 0}""");
        Assert.AreEqual(false, zero["result"]?.Value<bool>());

        var one = ScribanJsonTestHelper.RenderObject(template, """{"amount": 1}""");
        Assert.AreEqual(true, one["result"]?.Value<bool>());

        var other = ScribanJsonTestHelper.RenderObject(template, """{"amount": 9}""");
        Assert.AreEqual(JTokenType.Null, other["result"]?.Type);
    }

    [TestMethod]
    public void Parity_TransformWithStringTarget_EmitsTypedExpression()
    {
        // generateScriban: source='price', transform='value * 2', outputJson={"result":""}, inputJson={"price":5}
        const string template = """
            {
              "result": {{ $__t = (price * 2); ($__t == null ? null : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : (($__t | object.typeof) == "boolean" ? ($__t ? "true" : "false") : ("" + $__t)))) | json }},
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, """{"price": 5}""");
        Assert.AreEqual(JTokenType.String, result["result"]?.Type);
        Assert.AreEqual("10", result["result"]?.ToString());
    }

    [TestMethod]
    public void Parity_TransformWithNumberTarget_EmitsTypedExpression()
    {
        // generateScriban: source='amount', transform='value > 100', outputJson={"result":0}, inputJson={"amount":200}
        // bool result (true) → number via ($__t ? 1 : 0)
        const string template = """
            {
              "result": {{ $__t = (amount > 100); ($__t == null ? null : (($__t | object.typeof) == "boolean" ? ($__t ? 1 : 0) : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : ($__t | to_float)))) | json }},
            }
            """;
        var above = ScribanJsonTestHelper.RenderObject(template, """{"amount": 200}""");
        Assert.AreEqual(JTokenType.Integer, above["result"]?.Type);
        Assert.AreEqual(1, above["result"]?.Value<int>());

        var below = ScribanJsonTestHelper.RenderObject(template, """{"amount": 50}""");
        Assert.AreEqual(0, below["result"]?.Value<int>());
    }

    [TestMethod]
    public void Parity_IsRootSource_FieldInsideArrayLoopReadsFromRoot()
    {
        // generateScriban: ArrayMapping with one item field (item.Sku) and one
        // isRootSource field (OrderId — no alias prefix, reads from root object)
        const string template = """
            {
              "lines": [
              {{- for item in Items -}}
              {
                "sku": {{ item.Sku | json }},
                "orderId": {{ OrderId | json }},
              },
              {{- end -}}
              ],
            }
            """;
        const string inputJson = """
            {
              "OrderId": "ORD-99",
              "Items": [
                {"Sku": "AAA"},
                {"Sku": "BBB"}
              ]
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, inputJson);
        var lines = result["lines"] as JArray;
        Assert.IsNotNull(lines);
        Assert.AreEqual(2, lines.Count);
        // item field: different per row
        Assert.AreEqual("AAA", lines[0]["sku"]?.ToString());
        Assert.AreEqual("BBB", lines[1]["sku"]?.ToString());
        // root field: same value on every row
        Assert.AreEqual("ORD-99", lines[0]["orderId"]?.ToString());
        Assert.AreEqual("ORD-99", lines[1]["orderId"]?.ToString());
    }

    // ── Array with filter ──────────────────────────────────────────────────────
    // Config: source:Items → target:active, filter: Status == "active"

    [TestMethod]
    public void Parity_ArrayWithFilter_OnlyIncludesMatchingItems()
    {
        const string template = """
            {
              "active": [
              {{- for item in Items -}}
              {{- if item.Status == "active" -}}
              {
                "name": {{ item.Name | json }},
              },
              {{- end -}}
              {{- end -}}
              ],
            }
            """;
        const string inputJson = """
            {
              "Items": [
                {"Name": "Alice", "Status": "active"},
                {"Name": "Bob",   "Status": "inactive"},
                {"Name": "Carol", "Status": "active"}
              ]
            }
            """;
        var result = ScribanJsonTestHelper.RenderObject(template, inputJson);
        var active = result["active"] as JArray;
        Assert.IsNotNull(active);
        Assert.AreEqual(2, active.Count);
        Assert.AreEqual("Alice", active[0]["name"]?.ToString());
        Assert.AreEqual("Carol", active[1]["name"]?.ToString());
    }
}
