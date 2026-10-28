using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Ai;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Retry suggestions: a subscription's real failures go to the model masked, a policy comes back
/// that could be saved, tried on those failures, and the request is in the audit trail.
/// </summary>
[Collection("Http")]
public class RetrySuggestionTests(HttpFixture fixture) : IDisposable
{
    public void Dispose()
    {
        AiModels.ModelOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        admin.DeleteAsync("/api/settings/Bitween.Ai.ApiKey").GetAwaiter().GetResult();
    }

    sealed class FakeModel(string answer) : IAiModel
    {
        public List<AiPrompt> Asked { get; } = new();

        public Task<string> Complete(AiPrompt prompt)
        {
            Asked.Add(prompt);
            return Task.FromResult(answer);
        }
    }

    /// <summary>A subscription whose exchanges fail in mapping: "maybe" can't become a yes/no value.</summary>
    async Task<(HttpClient Admin, int SubscriptionId)> FailingSubscriptionAsync(int failures)
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}/savemapper", new
        {
            mapperId = "NativeMapper",
            mapperProperties = new[]
            {
                new
                {
                    key = "MappingRules",
                    value = """{ "version": 1, "fields": [ { "target": ["flag"], "from": { "kind": "path", "path": "order.flag" }, "type": "boolean" } ] }""",
                },
            },
        }));
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("default", key));
        for (var i = 0; i < failures; i++)
        {
            using var _ = await Api.CallSyncAsync(fixture, urlName, key, """{ "order": { "flag": "maybe", "owner": "lina@acme.example" } }""");
        }
        return (admin, subscriptionId);
    }

    async Task ConfigureAsync(HttpClient admin)
    {
        await Api.Json(await admin.PostAsJsonAsync("/api/settings/Bitween.Ai.ApiKey", new { value = "sk-test-key" }));
        var live = fixture.App.Services.GetService(typeof(BitweenOptions)) as BitweenOptions;
        for (var waited = 0; live!.Ai.ApiKey != "sk-test-key"; waited += 50)
        {
            Assert.True(waited < 10_000, "the AI key never took effect");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task A_policy_is_suggested_from_masked_failures_tried_on_them_and_audited()
    {
        var (admin, subscriptionId) = await FailingSubscriptionAsync(2);
        await ConfigureAsync(admin);
        var model = new FakeModel("""
            { "name": "Mapping stays failed", "summary": "S", "groups": [
              { "name": "Bad data", "appliesTo": ["Error"], "action": "Block",
                "matchers": [ { "type": "contains", "value": "boolean" } ] },
              { "name": "Outages", "appliesTo": ["Error"], "action": "Allow",
                "matchers": [ { "type": "contains", "value": "timed out" } ],
                "budget": { "maxAttemptsPerError": 3, "maxAttemptsTotal": 20, "delayStrategy": { "type": "fixed", "delayMs": 60000 } } } ] }
            """);
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/retrysuggestions", new { subscriptionId }));

        Assert.Equal("Mapping stays failed", (string)result["name"]!);
        Assert.Equal(2, result["groups"]!.AsArray().Count);
        Assert.Equal(2, (int)result["failures"]!);
        var row = Assert.Single(result["coverage"]!.AsArray());
        Assert.Equal(2, (int)row!["count"]!);
        Assert.Equal("Not retried: no retry policy", (string)row["now"]!);
        Assert.Equal("Not retried: Group 'Bad data' explicitly blocks this error", (string)row["suggested"]!);

        var sent = Assert.Single(model.Asked).Message;
        Assert.Equal(sent, (string)result["sent"]!);
        Assert.DoesNotContain("maybe", sent);
        Assert.DoesNotContain("lina@acme.example", sent);

        // What came back saves as it is.
        var saved = await admin.PostAsJsonAsync("/api/retrypolicies", new { name = Api.Unique("Suggested"), groups = result["groups"] });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var audited = await fixture.InDbAsync(db => db.Set<OperatorAction>().AsNoTracking()
            .AnyAsync(a => a.Action == OperatorAction.SuggestRetryPolicy && a.Target == $"subscription {subscriptionId}"));
        Assert.True(audited, "the suggestion wasn't recorded");
    }

    [Fact]
    public async Task A_subscription_without_failures_has_nothing_to_learn_from()
    {
        var (admin, _, _, _, subscriptionId) = await Api.GatewayAsync(fixture);
        await ConfigureAsync(admin);
        var model = new FakeModel("{}");
        AiModels.ModelOverride = _ => model;

        var response = await admin.PostAsJsonAsync("/api/retrysuggestions", new { subscriptionId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nothing to learn a policy from", await response.Content.ReadAsStringAsync());
        Assert.Empty(model.Asked);
    }
}
