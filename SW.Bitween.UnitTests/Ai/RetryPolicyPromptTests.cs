using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// Retry suggestions: what the model proposes is a policy that could be saved, it is tried on the
/// real errors here, and what is sent of them is masked.
/// </summary>
[TestClass]
public class RetryPolicyPromptTests
{
    const string Suggestion = """
        ```json
        { "name": "Retry outages", "summary": "S",
          "groups": [
            { "name": "Connection problems", "appliesTo": ["Error"], "action": "Allow",
              "matchers": [ { "type": "exceptionType", "value": "System.Net.Http.HttpRequestException" } ],
              "budget": { "maxAttemptsPerError": 3, "maxAttemptsTotal": 50,
                          "delayStrategy": { "type": "exponential", "initialDelayMs": 30000, "multiplier": 2, "maxDelayMs": 600000 } },
              "alertMode": "Send", "alertChannelId": 7, "notes": "Passing outages." },
            { "name": "Bad URL", "appliesTo": ["Error"], "action": "Block",
              "matchers": [ { "type": "contains", "value": "Invalid URI" } ],
              "budget": { "maxAttemptsPerError": 9, "delayStrategy": { "type": "fixed", "delayMs": 1000 } } }
          ] }
        ```
        """;

    [TestMethod]
    public void A_suggestion_is_a_policy_that_could_be_saved_without_its_alerts()
    {
        var suggestion = RetryPolicyPrompt.Parse(Suggestion);

        Assert.AreEqual("Retry outages", suggestion.Name);
        Assert.AreEqual(2, suggestion.Groups.Count);
        var outages = suggestion.Groups[0];
        Assert.AreEqual(0, outages.Priority);
        Assert.IsInstanceOfType<ExceptionTypeMatcher>(outages.Matchers[0]);
        Assert.IsInstanceOfType<ExponentialDelayStrategy>(outages.Budget!.DelayStrategy);
        Assert.AreEqual(RetryAlertMode.Inherit, outages.AlertMode);
        Assert.IsNull(outages.AlertChannelId);
        Assert.IsNull(suggestion.Groups[1].Budget, "a blocking group has no budget");
    }

    [TestMethod]
    public void A_suggestion_that_couldnt_be_saved_is_refused()
    {
        var e = Assert.ThrowsException<SWValidationException>(() => RetryPolicyPrompt.Parse("""
            { "groups": [ { "name": "No budget", "appliesTo": ["Error"], "action": "Allow",
                            "matchers": [ { "type": "contains", "value": "timeout" } ] } ] }
            """));
        StringAssert.Contains(e.Message, "can't be saved");

        Assert.ThrowsException<SWValidationException>(() => RetryPolicyPrompt.Parse("I would retry timeouts."));
    }

    [TestMethod]
    public async Task Each_failure_is_tried_on_the_policy_here()
    {
        var groups = RetryPolicyPrompt.Parse(Suggestion).Groups;

        Assert.AreEqual("Retried up to 3 times by \"Connection problems\"", await RetryPolicyPrompt.Outcome(groups,
            "System.Net.Http.HttpRequestException: Connection refused (skynet.example:443)\n   at Send()"));
        Assert.AreEqual("Not retried: Group 'Bad URL' explicitly blocks this error", await RetryPolicyPrompt.Outcome(groups,
            "System.UriFormatException: Invalid URI: The format of the URI could not be determined."));
        Assert.AreEqual("Not retried: no retry policy", await RetryPolicyPrompt.Outcome(null, "anything"));
    }

    [TestMethod]
    public void An_error_line_loses_its_values_and_addresses()
    {
        var masked = RetryPolicyPrompt.Mask("Response 503 from https://api.skynet.example/v2/orders for 'Lina Haddad' ref 88213");

        StringAssert.Contains(masked, "Response 503 from <url> for '<text>' ref <number>");
    }

    [TestMethod]
    public void The_current_policy_goes_without_its_alert_settings()
    {
        var current = new List<RetryGroup>
        {
            new()
            {
                Name = "Old", AppliesTo = [XchangeResultType.Error], Matchers = [new ContainsMatcher { Value = "timeout" }],
                Budget = new RetryBudget { MaxAttemptsPerError = 2, DelayStrategy = new FixedDelayStrategy { DelayMs = 1000 } },
                AlertHandlerId = "legacy", AlertHandlerProperties = new() { ["Password"] = "hunter2" }, AlertChannelId = 4,
            },
        };

        var message = RetryPolicyPrompt.Message("Subscription kind: Internal", 30, 12, 0,
            [new FailureKind("real", "System.TimeoutException: <text>", 12, 10, 9, DateTime.UtcNow, DateTime.UtcNow)], current);

        StringAssert.Contains(message, "\"value\":\"timeout\"");
        StringAssert.Contains(message, "Retried 10 times; the retry succeeded 9 times.");
        Assert.IsFalse(message.Contains("hunter2") || message.Contains("legacy") || message.Contains("alertChannelId"), message);
        Assert.IsFalse(message.Contains("real"), "the real error text is never sent");
    }
}
