using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class ScribanJsonHelperLookupAndTypeRuleTests
{
    [TestMethod]
    public void LookupMapping_NullFallback_WhenHit_ReturnsMappedValue()
    {
        var input = "{\"status\":\"A\"}";
        var template = "{ \"state\": {{ $__e = { \"A\": \"Active\", \"B\": \"Blocked\" }; $__e[status] | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"state\":\"Active\"}", output);
    }

    [TestMethod]
    public void LookupMapping_NullFallback_WhenMiss_ReturnsNull()
    {
        var input = "{\"status\":\"Z\"}";
        var template = "{ \"state\": {{ $__e = { \"A\": \"Active\" }; $__e[status] | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"state\":null}", output);
    }

    [TestMethod]
    public void LookupMapping_CustomFallback_WhenMiss_ReturnsCustomValue()
    {
        var input = "{\"status\":\"Z\"}";
        var template = "{ \"state\": {{ $__e = { \"A\": \"Active\" }; ($__e[status] ?? \"Unknown\") | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"state\":\"Unknown\"}", output);
    }

    [TestMethod]
    public void LookupMapping_RespectsNumberTargetValueType()
    {
        var input = "{\"code\":\"A\"}";
        var template = "{ \"mapped\": {{ $__e = { \"A\": 1, \"B\": 2 }; $__e[code] | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual(JTokenType.Integer, output["mapped"]?.Type);
        Assert.AreEqual(1, output["mapped"]?.Value<int>());
    }

    [TestMethod]
    public void LookupMapping_RespectsBooleanTargetValueType()
    {
        var input = "{\"code\":\"A\"}";
        var template = "{ \"mapped\": {{ $__e = { \"A\": true, \"B\": false }; $__e[code] | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual(JTokenType.Boolean, output["mapped"]?.Type);
        Assert.AreEqual(true, output["mapped"]?.Value<bool>());
    }

    [TestMethod]
    public void TransformMapping_AppliesMathExpression()
    {
        var input = "{\"price\":100}";
        var template = "{ \"total\": {{ (price * 1.1) | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual(110, output["total"]?.Value<int>());
    }

    [TestMethod]
    public void TransformMapping_AppliesTypedRule_BoolToNumber()
    {
        var input = "{\"amount\":200}";
        var expr = "{{ $__t = (amount > 100); ($__t == null ? null : (($__t | object.typeof) == \"boolean\" ? ($__t ? 1 : 0) : (((($__t | object.typeof) == \"object\" || ($__t | object.typeof) == \"array\") ? null : ($__t | to_float)))) ) | json }}";

        var output = RenderValue(expr, input);

        Assert.AreEqual(JTokenType.Integer, output.Type);
        Assert.AreEqual(1, output.Value<int>());
    }

    [TestMethod]
    public void TransformMapping_AppliesTypedRule_BoolToString()
    {
        var input = "{\"amount\":200}";
        var expr = "{{ $__t = (amount > 100); ($__t == null ? null : (((($__t | object.typeof) == \"object\" || ($__t | object.typeof) == \"array\") ? null : (($__t | object.typeof) == \"boolean\" ? ($__t ? \"true\" : \"false\") : (\"\" + $__t)))) ) | json }}";

        var output = RenderValue(expr, input);

        Assert.AreEqual(JTokenType.String, output.Type);
        Assert.AreEqual("true", output.Value<string>());
    }

    [TestMethod]
    public void TypeRules_BoolToString_And_BoolToNumber()
    {
        var input = "{\"flag\":true,\"flag2\":false}";

        var boolToString = RenderValue("{{ (flag == null ? null : (flag ? \"true\" : \"false\")) | json }}", input);
        var boolToNumber = RenderValue("{{ (flag2 == null ? null : (flag2 ? 1 : 0)) | json }}", input);

        Assert.AreEqual("true", boolToString.Value<string>());
        Assert.AreEqual(0, boolToNumber.Value<int>());
    }

    [TestMethod]
    public void TypeRules_StringToBool_IsNull()
    {
        var input = "{\"name\":\"x\"}";

        var value = RenderValue("{{ null | json }}", input);

        Assert.AreEqual(JTokenType.Null, value.Type);
    }

    [TestMethod]
    public void TypeRules_NumberToBool_ZeroFalse_OneTrue_OtherNull()
    {
        var input = "{\"a\":0,\"b\":1,\"c\":9}";

        var a = RenderValue("{{ (a == 0 ? false : (a == 1 ? true : null)) | json }}", input);
        var b = RenderValue("{{ (b == 0 ? false : (b == 1 ? true : null)) | json }}", input);
        var c = RenderValue("{{ (c == 0 ? false : (c == 1 ? true : null)) | json }}", input);

        Assert.AreEqual(false, a.Value<bool>());
        Assert.AreEqual(true, b.Value<bool>());
        Assert.AreEqual(JTokenType.Null, c.Type);
    }

    [TestMethod]
    public void TypeRules_StringToNumber_InvalidIsNull()
    {
        var input = "{\"a\":\"x\"}";

        var value = RenderValue("{{ (a | to_float) | json }}", input);

        Assert.AreEqual(JTokenType.Null, value.Type);
    }

    [TestMethod]
    public void TypeRules_NumberToString_WrapsInQuotes()
    {
        var input = "{\"n\":42}";

        var value = RenderValue("{{ (\"\" + n) | json }}", input);

        Assert.AreEqual(JTokenType.String, value.Type);
        Assert.AreEqual("42", value.Value<string>());
    }
}
