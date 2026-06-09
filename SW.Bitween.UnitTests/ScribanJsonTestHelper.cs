using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.JsonMapper;

namespace SW.Bitween.UnitTests;

internal static class ScribanJsonTestHelper
{
    public static string Render(string template, string inputJson) =>
        ScribanJsonHelper.Render(template, inputJson);

    public static JObject RenderObject(string template, string inputJson) =>
        JObject.Parse(Render(template, inputJson));

    public static JArray RenderArray(string template, string inputJson) =>
        JArray.Parse(Render(template, inputJson));

    public static JToken RenderValue(string valueExpression, string inputJson)
    {
        var template = "{ \"value\": " + valueExpression + " }";
        return RenderObject(template, inputJson)["value"]!;
    }

    public static void AssertJsonEquals(string expectedJson, string actualJson)
    {
        var expected = JToken.Parse(expectedJson);
        var actual = JToken.Parse(actualJson);

        Assert.IsTrue(
            JToken.DeepEquals(expected, actual),
            $"Expected JSON:\n{expected.ToString(Formatting.Indented)}\n\nActual JSON:\n{actual.ToString(Formatting.Indented)}");
    }
}
