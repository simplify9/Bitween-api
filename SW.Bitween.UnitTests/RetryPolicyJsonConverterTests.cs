using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Bitween.JsonConverters;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class RetryPolicyJsonConverterTests
{
    private static JsonSerializer BuildSerializer()
    {
        var serializer = new JsonSerializer();
        serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        serializer.Converters.Add(new MatcherJsonConverter());
        serializer.Converters.Add(new DelayStrategyJsonConverter());
        return serializer;
    }

    private static T RoundTrip<T>(T value, JsonSerializer serializer)
    {
        string json;
        using (var sw = new StringWriter())
        using (JsonWriter writer = new JsonTextWriter(sw))
        {
            serializer.Serialize(writer, value);
            json = sw.ToString();
        }

        using var sr = new StringReader(json);
        using JsonReader reader = new JsonTextReader(sr);
        return serializer.Deserialize<T>(reader);
    }

    [TestMethod]
    public void ContainsMatcher_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        Matcher original = new ContainsMatcher { Value = "timeout", CaseSensitive = true };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<ContainsMatcher>(result);
        Assert.AreEqual("timeout", typed.Value);
        Assert.IsTrue(typed.CaseSensitive);
    }

    [TestMethod]
    public void RegexMatcher_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        Matcher original = new RegexMatcher { Pattern = "connect.*failed", Flags = "" };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<RegexMatcher>(result);
        Assert.AreEqual("connect.*failed", typed.Pattern);
        Assert.AreEqual("", typed.Flags);
    }

    [TestMethod]
    public void ExceptionTypeMatcher_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        Matcher original = new ExceptionTypeMatcher { Value = "System.TimeoutException", IncludeInner = false };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<ExceptionTypeMatcher>(result);
        Assert.AreEqual("System.TimeoutException", typed.Value);
        Assert.IsFalse(typed.IncludeInner);
    }

    [TestMethod]
    public void JsonPathMatcher_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        Matcher original = new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "500" };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<JsonPathMatcher>(result);
        Assert.AreEqual("$.error.code", typed.Path);
        Assert.AreEqual(JsonPathOp.Eq, typed.Op);
        Assert.AreEqual("500", typed.Value);
    }

    [TestMethod]
    public void FixedDelayStrategy_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        DelayStrategy original = new FixedDelayStrategy { DelayMs = 5000 };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<FixedDelayStrategy>(result);
        Assert.AreEqual(5000, typed.DelayMs);
    }

    [TestMethod]
    public void LinearDelayStrategy_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        DelayStrategy original = new LinearDelayStrategy { InitialDelayMs = 1000, IncrementMs = 500 };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<LinearDelayStrategy>(result);
        Assert.AreEqual(1000, typed.InitialDelayMs);
        Assert.AreEqual(500, typed.IncrementMs);
    }

    [TestMethod]
    public void ExponentialDelayStrategy_round_trips_through_newtonsoft()
    {
        var serializer = BuildSerializer();
        DelayStrategy original = new ExponentialDelayStrategy { InitialDelayMs = 1000, Multiplier = 3.0, MaxDelayMs = 60_000 };

        var result = RoundTrip(original, serializer);

        var typed = Assert1<ExponentialDelayStrategy>(result);
        Assert.AreEqual(1000, typed.InitialDelayMs);
        Assert.AreEqual(3.0, typed.Multiplier);
        Assert.AreEqual(60_000, typed.MaxDelayMs);
    }

    [TestMethod]
    public void RetryGroup_with_nested_matcher_and_delay_strategy_round_trips()
    {
        var serializer = BuildSerializer();
        var original = new RetryGroup
        {
            Name = "Timeout Group",
            Priority = 10,
            AppliesTo = [XchangeResultType.Error],
            Matchers = [new ContainsMatcher { Value = "timeout" }],
            Action = RetryAction.Allow,
            Budget = new RetryBudget
            {
                MaxAttemptsPerError = 3,
                MaxAttemptsTotal = 10,
                DelayStrategy = new ExponentialDelayStrategy { InitialDelayMs = 1000, Multiplier = 2, MaxDelayMs = 30_000 }
            }
        };

        var result = RoundTrip(original, serializer);

        Assert.IsNotNull(result);
        Assert.AreEqual("Timeout Group", result.Name);
        Assert.AreEqual(RetryAction.Allow, result.Action);
        Assert.AreEqual(1, result.Matchers.Count);
        Assert.IsInstanceOfType(result.Matchers[0], typeof(ContainsMatcher));
        Assert.IsInstanceOfType(result.Budget!.DelayStrategy, typeof(ExponentialDelayStrategy));
    }

    private static T Assert1<T>(object value) where T : class
    {
        Assert.IsInstanceOfType(value, typeof(T));
        return (T)value;
    }
}
