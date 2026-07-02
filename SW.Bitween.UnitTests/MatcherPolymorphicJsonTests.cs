using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class MatcherPolymorphicJsonTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    [TestMethod]
    public void JsonPathMatcher_round_trips_with_DefaultJsonTypeInfoResolver()
    {
        var matchers = new List<Matcher>
        {
            new ExceptionTypeMatcher { Value = "System.TimeoutException" },
            new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "500" }
        };

        var json = JsonSerializer.Serialize(matchers, Opts);
        Assert.IsTrue(json.Contains("\"type\""), string.Format("No discriminator in JSON: {0}", json));

        var roundTripped = JsonSerializer.Deserialize<List<Matcher>>(json, Opts);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(2, roundTripped.Count);
        Assert.IsInstanceOfType(roundTripped[0], typeof(ExceptionTypeMatcher));
        Assert.IsInstanceOfType(roundTripped[1], typeof(JsonPathMatcher));
    }
}
