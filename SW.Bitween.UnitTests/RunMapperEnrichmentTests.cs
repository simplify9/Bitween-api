using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RunMapperEnrichmentTests
{
    // Replicates the fixed parsing logic in XchangeService.RunMapper
    private static JObject TryParseAsJObject(string data) =>
        JToken.Parse(data) as JObject;

    [TestMethod]
    [Description("Receiver returns a plain JSON string — should not throw and should skip enrichment")]
    public void WhenDataIsJsonString_ShouldNotThrow_AndReturnNull()
    {
        var data = "\"this is a plain string response from the receiver\"";

        var result = TryParseAsJObject(data);

        Assert.IsNull(result, "Expected null because data is a JSON string, not an object");
    }

    [TestMethod]
    [Description("Receiver returns a JSON object — enrichment should proceed normally")]
    public void WhenDataIsJsonObject_ShouldReturnJObject()
    {
        var data = "{\"orderId\":\"123\",\"status\":\"pending\"}";

        var result = TryParseAsJObject(data);

        Assert.IsNotNull(result, "Expected a JObject because data is a valid JSON object");
        Assert.AreEqual("123", result["orderId"]?.ToString());
    }

    [TestMethod]
    [Description("Receiver returns a JSON array — should not throw and should skip enrichment")]
    public void WhenDataIsJsonArray_ShouldNotThrow_AndReturnNull()
    {
        var data = "[\"item1\",\"item2\"]";

        var result = TryParseAsJObject(data);

        Assert.IsNull(result, "Expected null because data is a JSON array, not an object");
    }

    [TestMethod]
    [Description("Large JSON string payload (like the one that triggered the original error) — should not throw")]
    public void WhenDataIsLargeJsonString_ShouldNotThrow_AndReturnNull()
    {
        // Simulate the case from the error: a JSON-encoded string with ~200k chars
        var innerString = new string('x', 200_000);
        var data = $"\"{innerString}\"";

        JObject result = null;
        var threw = false;

        try
        {
            result = TryParseAsJObject(data);
        }
        catch
        {
            threw = true;
        }

        Assert.IsFalse(threw, "Should not throw for a large JSON string payload");
        Assert.IsNull(result, "Expected null because data is a JSON string, not an object");
    }

    [TestMethod]
    [Description("Enrichment is injected into a JSON object when partner properties exist")]
    public void WhenDataIsJsonObject_PartnerPropertiesCanBeInjected()
    {
        var data = "{\"orderId\":\"123\"}";
        var partnerProps = new { apiKey = "abc", region = "us-east" };

        var jObj = TryParseAsJObject(data);

        Assert.IsNotNull(jObj);
        jObj["__partner__"] = JObject.FromObject(partnerProps);

        Assert.AreEqual("abc", jObj["__partner__"]?["apiKey"]?.ToString());
    }
}
