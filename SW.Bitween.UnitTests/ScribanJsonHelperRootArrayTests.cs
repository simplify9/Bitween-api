using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using static SW.Bitween.UnitTests.ScribanJsonTestHelper;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Tests for root-level array support:
///   A) Root array INPUT  → object output
///   B) Root object input → root array OUTPUT
///   C) Root array INPUT  → root array OUTPUT
/// </summary>
[TestClass]
public class ScribanJsonHelperRootArrayTests
{
    // ─── A. Root array input → object output ─────────────────────────────────

    [TestMethod]
    public void RootArrayInput_AccessFirstItemField_DirectReference()
    {
        // Hoisted top-level keys — direct field access without index
        var input = "[{\"OrderId\":1,\"OrderNumber\":\"ORD-001\"}]";
        var template = "{ \"id\": {{ OrderId | json }}, \"ref\": {{ OrderNumber | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual(1L, output["id"]?.Value<long>());
        Assert.AreEqual("ORD-001", output["ref"]?.Value<string>());
    }

    [TestMethod]
    public void RootArrayInput_AccessFirstItemField_ViaItemsIndex()
    {
        var input = "[{\"OrderId\":1}]";
        var template = "{ \"id\": {{ items[0].OrderId | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual(1L, output["id"]?.Value<long>());
    }

    [TestMethod]
    public void RootArrayInput_IterateAllItems_ViaItemsLoop()
    {
        var input = "[{\"OrderId\":1},{\"OrderId\":2},{\"OrderId\":3}]";
        var template =
            "{ \"ids\": [" +
            "{{ for item in items }}" +
            "{{ item.OrderId | json }}," +
            "{{ end }}" +
            "] }";

        var output = RenderObject(template, input);
        var ids = output["ids"] as JArray;

        Assert.IsNotNull(ids);
        Assert.AreEqual(3, ids!.Count);
        Assert.AreEqual(1L, ids[0].Value<long>());
        Assert.AreEqual(2L, ids[1].Value<long>());
        Assert.AreEqual(3L, ids[2].Value<long>());
    }

    [TestMethod]
    public void RootArrayInput_EmptyArray_ProducesEmptyItems()
    {
        var input = "[]";
        var template = "{ \"count\": {{ items.size | json }} }";

        // Should not throw; items should be an empty array
        var output = Render(template, input);

        Assert.IsNotNull(output);
    }

    [TestMethod]
    public void RootArrayInput_PascalCaseAndCamelCaseAliasBothWork()
    {
        var input = "[{\"OrderId\":99}]";
        var templatePascal = "{ \"a\": {{ OrderId | json }} }";
        var templateCamel = "{ \"a\": {{ orderId | json }} }";

        var a = RenderObject(templatePascal, input)["a"]?.Value<long>();
        var b = RenderObject(templateCamel, input)["a"]?.Value<long>();

        Assert.AreEqual(99L, a);
        Assert.AreEqual(99L, b);
    }

    [TestMethod]
    public void RootArrayInput_AllFourModes_SourceFixedPartnerGlobal()
    {
        var input =
            "[{\"OrderId\":7,\"Status\":\"A\"}," +
            "{\"OrderId\":8,\"Status\":\"B\"}]";

        // Embed __partner__ / __globals__ via hoisted fields — simulate injection
        var inputWithExtras =
            "[{\"OrderId\":7,\"Status\":\"A\",\"__partner__\":{\"key\":\"PK\"},\"__globals__\":{\"S\":{\"K\":\"GV\"}}}]";

        var template =
            "{ " +
            "\"fromSource\": {{ OrderId | json }}, " +
            "\"fromFixed\": \"FX\", " +
            "\"fromPartner\": {{ __partner__?.key | json }}, " +
            "\"fromGlobal\": {{ __globals__?.S[\"K\"] | json }} " +
            "}";

        var output = RenderObject(template, inputWithExtras);

        Assert.AreEqual(7L, output["fromSource"]?.Value<long>());
        Assert.AreEqual("FX", output["fromFixed"]?.Value<string>());
        Assert.AreEqual("PK", output["fromPartner"]?.Value<string>());
        Assert.AreEqual("GV", output["fromGlobal"]?.Value<string>());
    }

    [TestMethod]
    public void RootArrayInput_LookupOnHoistedField()
    {
        var input = "[{\"Status\":\"A\"}]";
        var template = "{ \"state\": {{ $__e = { \"A\": \"Active\", \"B\": \"Blocked\" }; $__e[Status] | json }} }";

        var output = RenderObject(template, input);

        Assert.AreEqual("Active", output["state"]?.Value<string>());
    }

    // ─── B. Root object input → root array output ─────────────────────────────

    [TestMethod]
    public void RootObjectInput_TemplateProducesRootArray_IsValidJson()
    {
        var input = "{\"items\":[{\"id\":1},{\"id\":2}]}";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{ \"id\": {{ item.id | json }} }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(1L, output[0]["id"]?.Value<long>());
        Assert.AreEqual(2L, output[1]["id"]?.Value<long>());
    }

    [TestMethod]
    public void RootObjectInput_RootArrayOutput_AllFourModes()
    {
        var input =
            "{\"items\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]," +
            "\"__partner__\":{\"env\":\"prod\"}," +
            "\"__globals__\":{\"S\":{\"K\":\"gval\"}}}";

        var template =
            "[" +
            "{{ for item in items }}" +
            "{ " +
            "\"sku\": {{ item.sku | json }}, " +
            "\"fixed\": \"FX\", " +
            "\"partner\": {{ __partner__?.env | json }}, " +
            "\"global\": {{ __globals__?.S[\"K\"] | json }} " +
            "}," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual("A", output[0]["sku"]?.Value<string>());
        Assert.AreEqual("FX", output[0]["fixed"]?.Value<string>());
        Assert.AreEqual("prod", output[0]["partner"]?.Value<string>());
        Assert.AreEqual("gval", output[0]["global"]?.Value<string>());
        Assert.AreEqual("B", output[1]["sku"]?.Value<string>());
    }

    [TestMethod]
    public void RootObjectInput_RootArrayOutput_LookupOnItemField()
    {
        var input = "{\"items\":[{\"code\":\"A\"},{\"code\":\"B\"},{\"code\":\"Z\"}]}";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{ \"label\": {{ $__e = { \"A\": \"Alpha\", \"B\": \"Beta\" }; ($__e[item.code] ?? \"Unknown\") | json }} }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual("Alpha", output[0]["label"]?.Value<string>());
        Assert.AreEqual("Beta", output[1]["label"]?.Value<string>());
        Assert.AreEqual("Unknown", output[2]["label"]?.Value<string>());
    }

    [TestMethod]
    public void RootObjectInput_RootPrimitiveArray_FromSourceField()
    {
        var input = "{\"tags\":[\"x\",\"y\",\"z\"]}";
        var template =
            "[" +
            "{{ for t in tags }}" +
            "{{ t | json }}," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(3, output.Count);
        Assert.AreEqual("x", output[0].Value<string>());
        Assert.AreEqual("y", output[1].Value<string>());
        Assert.AreEqual("z", output[2].Value<string>());
    }

    [TestMethod]
    public void RootObjectInput_RootArrayOutput_EmptySourceArray_ProducesEmptyArray()
    {
        var input = "{\"items\":[]}";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{ \"id\": {{ item.id | json }} }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(0, output.Count);
    }

    [TestMethod]
    public void RootObjectInput_RootArrayOutput_WithFilter()
    {
        var input = "{\"items\":[{\"id\":1,\"active\":true},{\"id\":2,\"active\":false}]}";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{{ if item.active == true }}" +
            "{ \"id\": {{ item.id | json }} }," +
            "{{ end }}" +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual(1L, output[0]["id"]?.Value<long>());
    }

    // ─── C. Root array input → root array output ──────────────────────────────

    [TestMethod]
    public void RootArrayInput_RootArrayOutput_IteratesItemsVariable()
    {
        var input = "[{\"OrderId\":1,\"Status\":\"A\"},{\"OrderId\":2,\"Status\":\"B\"}]";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{ \"id\": {{ item.OrderId | json }}, \"status\": {{ item.Status | json }} }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(1L, output[0]["id"]?.Value<long>());
        Assert.AreEqual("A", output[0]["status"]?.Value<string>());
        Assert.AreEqual(2L, output[1]["id"]?.Value<long>());
        Assert.AreEqual("B", output[1]["status"]?.Value<string>());
    }

    [TestMethod]
    public void RootArrayInput_RootArrayOutput_AllFourModes()
    {
        var input =
            "[{" +
            "\"OrderId\":5,\"Status\":\"A\"," +
            "\"__partner__\":{\"env\":\"prod\"}," +
            "\"__globals__\":{\"S\":{\"K\":\"gval\"}}" +
            "}]";

        var template =
            "[" +
            "{{ for item in items }}" +
            "{ " +
            "\"id\": {{ item.OrderId | json }}, " +
            "\"fixed\": \"FX\", " +
            "\"partner\": {{ __partner__?.env | json }}, " +
            "\"global\": {{ __globals__?.S[\"K\"] | json }} " +
            "}," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual(5L, output[0]["id"]?.Value<long>());
        Assert.AreEqual("FX", output[0]["fixed"]?.Value<string>());
        Assert.AreEqual("prod", output[0]["partner"]?.Value<string>());
        Assert.AreEqual("gval", output[0]["global"]?.Value<string>());
    }

    [TestMethod]
    public void RootArrayInput_RootArrayOutput_LookupOnItemField()
    {
        var input = "[{\"Status\":\"A\"},{\"Status\":\"B\"},{\"Status\":\"Z\"}]";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{ \"label\": {{ $__e = { \"A\": \"Active\", \"B\": \"Blocked\" }; ($__e[item.Status] ?? \"Unknown\") | json }} }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(3, output.Count);
        Assert.AreEqual("Active", output[0]["label"]?.Value<string>());
        Assert.AreEqual("Blocked", output[1]["label"]?.Value<string>());
        Assert.AreEqual("Unknown", output[2]["label"]?.Value<string>());
    }

    [TestMethod]
    public void RootArrayInput_RootPrimitiveArray_ExtractsSingleFieldFromEachItem()
    {
        var input = "[{\"OrderId\":10},{\"OrderId\":20},{\"OrderId\":30}]";
        var template =
            "[" +
            "{{ for item in items }}" +
            "{{ item.OrderId | json }}," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(3, output.Count);
        Assert.AreEqual(10L, output[0].Value<long>());
        Assert.AreEqual(20L, output[1].Value<long>());
        Assert.AreEqual(30L, output[2].Value<long>());
    }

    [TestMethod]
    public void RootArrayInput_RootArrayOutput_NestedArrayInsideItems()
    {
        var input = "[{\"OrderId\":1,\"OrderItems\":[{\"SKU\":\"A\"},{\"SKU\":\"B\"}]}]";
        var template =
            "[" +
            "{{ for order in items }}" +
            "{ \"orderId\": {{ order.OrderId | json }}, \"skus\": [" +
            "{{ for li in order.OrderItems }}" +
            "{{ li.SKU | json }}," +
            "{{ end }}" +
            "] }," +
            "{{ end }}" +
            "]";

        var output = RenderArray(template, input);

        Assert.AreEqual(1, output.Count);
        Assert.AreEqual(1L, output[0]["orderId"]?.Value<long>());
        var skus = output[0]["skus"] as JArray;
        Assert.IsNotNull(skus);
        Assert.AreEqual("A", skus![0].Value<string>());
        Assert.AreEqual("B", skus![1].Value<string>());
    }
}
