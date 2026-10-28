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
/// Explain this failure: a real failed exchange goes to the model with its values masked, the
/// answer comes back in three parts, and the request is in the audit trail.
/// </summary>
[Collection("Http")]
public class FailureExplanationTests(HttpFixture fixture) : IDisposable
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

    /// <summary>An exchange that fails in mapping: "maybe" can't become a yes/no value.</summary>
    async Task<(HttpClient Admin, string XchangeId)> FailedExchangeAsync()
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
        using var _ = await Api.CallSyncAsync(fixture, urlName, key, """{ "order": { "flag": "maybe", "owner": "lina@acme.example" } }""");

        var found = await Api.Json(await admin.GetAsync(
            $"/api/xchanges?filter={Uri.EscapeDataString($"SubscriptionId:1:{subscriptionId}")}&size=1"));
        return (admin, (string)found["result"]![0]!["id"]!);
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
    public async Task A_failed_exchange_is_explained_from_masked_data_and_audited()
    {
        var (admin, xchangeId) = await FailedExchangeAsync();
        await ConfigureAsync(admin);
        var model = new FakeModel("""{ "summary": "S", "cause": "C", "fix": "F" }""");
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/xchangeexplanations", new { xchangeId }));

        Assert.Equal("S", (string)result["summary"]!);
        Assert.Equal("F", (string)result["fix"]!);
        var sent = Assert.Single(model.Asked).Message;
        Assert.Equal(sent, (string)result["sent"]!);
        Assert.Contains("Failed step: mapping", sent);
        Assert.Contains("\"flag\": \"<text>\"", sent);
        Assert.DoesNotContain("maybe", sent);
        Assert.DoesNotContain("lina@acme.example", sent);

        var audited = await fixture.InDbAsync(db => db.Set<OperatorAction>().AsNoTracking()
            .AnyAsync(a => a.Action == OperatorAction.ExplainFailure && a.Target == xchangeId));
        Assert.True(audited, "the explanation wasn't recorded");
    }

    [Fact]
    public async Task Without_AI_set_up_nothing_is_read_or_sent()
    {
        var (admin, xchangeId) = await FailedExchangeAsync();

        var response = await admin.PostAsJsonAsync("/api/xchangeexplanations", new { xchangeId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Settings, AI", await response.Content.ReadAsStringAsync());
    }
}
