using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

[TestClass]
public class ScribanJsonHelperArrayMappingTests
{
    [TestMethod]
    public void ObjectArrayMapping_SupportsSourceLookupTransformFixedPartnerGlobal()
    {
        var input = "{\"items\":[{\"sku\":\"A\",\"price\":100},{\"sku\":\"B\",\"price\":50}],\"__partner__\":{\"apiKey\":\"p-1\"},\"__globals__\":{\"S\":{\"K\":\"g-1\"}}}";
        var template =
            "{ \"out\": [" +
            "{{ for item in items }}" +
            "{ " +
            "\"fromSource\": {{ item.sku | json }}, " +
            "\"fromLookup\": {{ $__e = { \"A\": \"Alpha\", \"B\": \"Beta\" }; $__e[item.sku] | json }}, " +
            "\"fromTransform\": {{ (item.price * 2) | json }}, " +
            "\"fromFixed\": \"STATIC\", " +
            "\"fromPartner\": {{ __partner__?.apiKey | json }}, " +
            "\"fromGlobal\": {{ __globals__?.S[\"K\"] | json }} " +
            "}," +
            "{{ end }}" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals(
            "{\"out\":[{" +
            "\"fromSource\":\"A\",\"fromLookup\":\"Alpha\",\"fromTransform\":200,\"fromFixed\":\"STATIC\",\"fromPartner\":\"p-1\",\"fromGlobal\":\"g-1\"},{" +
            "\"fromSource\":\"B\",\"fromLookup\":\"Beta\",\"fromTransform\":100,\"fromFixed\":\"STATIC\",\"fromPartner\":\"p-1\",\"fromGlobal\":\"g-1\"}]}",
            output);
    }

    [TestMethod]
    public void ObjectArrayMapping_SupportsFilter()
    {
        var input = "{\"items\":[{\"id\":1,\"active\":true},{\"id\":2,\"active\":false}]}";
        var template =
            "{ \"out\": [" +
            "{{ for item in items }}" +
            "{{ if item.active == true }}" +
            "{ \"id\": {{ item.id | json }} }," +
            "{{ end }}" +
            "{{ end }}" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals("{\"out\":[{\"id\":1}]}", output);
    }

    [TestMethod]
    public void ObjectArrayMapping_HandlesNestedObjectsInsideItems()
    {
        var input = "{\"items\":[{\"customer\":{\"address\":{\"city\":\"Amman\"}}}]}";
        var template =
            "{ \"out\": [" +
            "{{ for item in items }}" +
            "{ \"city\": {{ item.customer.address.city | json }} }," +
            "{{ end }}" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals("{\"out\":[{\"city\":\"Amman\"}]}", output);
    }

    [TestMethod]
    public void PrimitiveArrayMapping_SupportsSourceFixedPartnerGlobal()
    {
        var input = "{\"tags\":[\"a\",\"b\"],\"__partner__\":{\"k\":\"p\"},\"__globals__\":{\"S\":{\"K\":\"g\"}}}";
        var template =
            "{ \"out\": [" +
            "{{ for tag in tags }}" +
            "{ \"source\": {{ tag | json }}, \"fixed\": \"FX\", \"partner\": {{ __partner__?.k | json }}, \"global\": {{ __globals__?.S[\"K\"] | json }} }," +
            "{{ end }}" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals("{\"out\":[{\"source\":\"a\",\"fixed\":\"FX\",\"partner\":\"p\",\"global\":\"g\"},{\"source\":\"b\",\"fixed\":\"FX\",\"partner\":\"p\",\"global\":\"g\"}]}", output);
    }

    [TestMethod]
    public void FixedArrayItems_CanIncludeSourcePartnerAndGlobalExpressions()
    {
        var input = "{\"orderId\":\"C-1\",\"__partner__\":{\"env\":\"prod\"},\"__globals__\":{\"A\":{\"B\":\"v1\"}}}";
        var template =
            "{ \"out\": [" +
            "{ \"name\": \"order\", \"value\": {{ orderId | json }} }," +
            "{ \"name\": \"env\", \"value\": {{ __partner__?.env | json }} }," +
            "{ \"name\": \"global\", \"value\": {{ __globals__?.A[\"B\"] | json }} }," +
            "{ \"name\": \"fixed\", \"value\": \"x\" }" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals("{\"out\":[{\"name\":\"order\",\"value\":\"C-1\"},{\"name\":\"env\",\"value\":\"prod\"},{\"name\":\"global\",\"value\":\"v1\"},{\"name\":\"fixed\",\"value\":\"x\"}]}", output);
    }

    [TestMethod]
    public void FixedArrayItems_AddAndEditScenario_ReflectsUpdatedTemplate()
    {
        var input = "{\"orderId\":\"C-1\"}";
        var beforeTemplate = "{ \"out\": [ { \"name\": \"order\", \"value\": {{ orderId | json }} } ] }";
        var afterTemplate = "{ \"out\": [ { \"name\": \"order\", \"value\": \"edited\" }, { \"name\": \"new\", \"value\": 2 } ] }";

        var before = Render(beforeTemplate, input);
        var after = Render(afterTemplate, input);

        AssertJsonEquals("{\"out\":[{\"name\":\"order\",\"value\":\"C-1\"}]}", before);
        AssertJsonEquals("{\"out\":[{\"name\":\"order\",\"value\":\"edited\"},{\"name\":\"new\",\"value\":2}]}", after);
    }

    [TestMethod]
    public void NestedArrays_ThreeLevels_AreRenderedCorrectly()
    {
        var input =
            "{" +
            "\"companies\":[{" +
            "\"name\":\"Acme\"," +
            "\"departments\":[{" +
            "\"name\":\"IT\"," +
            "\"employees\":[{\"name\":\"Alice\"},{\"name\":\"Bob\"}]" +
            "}]" +
            "}]" +
            "}";

        var template =
            "{ \"out\": [" +
            "{{ for co in companies }}" +
            "{ \"company\": {{ co.name | json }}, \"departments\": [" +
            "{{ for dep in co.departments }}" +
            "{ \"department\": {{ dep.name | json }}, \"staff\": [" +
            "{{ for emp in dep.employees }}" +
            "{ \"employee\": {{ emp.name | json }} }," +
            "{{ end }}" +
            "] }," +
            "{{ end }}" +
            "] }," +
            "{{ end }}" +
            "] }";

        var output = Render(template, input);

        AssertJsonEquals(
            "{\"out\":[{\"company\":\"Acme\",\"departments\":[{\"department\":\"IT\",\"staff\":[{\"employee\":\"Alice\"},{\"employee\":\"Bob\"}]}]}]}",
            output);
    }

    [TestMethod]
    public void SmartArray_AllowsFirstItemMemberAccessWithoutIndex()
    {
        var input = "{\"items\":[{\"city\":\"Amman\"}]}";
        var template = "{ \"city\": {{ items.city | json }} }";

        var output = Render(template, input);

        AssertJsonEquals("{\"city\":\"Amman\"}", output);
    }

    [TestMethod]
    public void EmptyArray_ProducesEmptyOutputArray()
    {
        var input = "{\"items\":[]}";
        var template = "{ \"out\": [{{ for item in items }}{\"x\":1},{{ end }}] }";

        var output = Render(template, input);

        AssertJsonEquals("{\"out\":[]}", output);
    }

    [TestMethod]
    public void TrailingCommas_AreRemovedFromRenderedJson()
    {
        var input = "{\"items\":[1,2]}";
        var template = "{ \"arr\": [{{ for i in items }}{{ i | json }},{{ end }}], }";

        var output = Render(template, input);

        AssertJsonEquals("{\"arr\":[1,2]}", output);
    }
}
