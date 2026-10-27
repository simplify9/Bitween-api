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
/// Ask your exchanges: a question goes to the model with names and the time, never an exchange,
/// and comes back as the Exchanges page's filter, recorded in the audit trail.
/// </summary>
[Collection("Http")]
public class AskExchangesTests(HttpFixture fixture) : IDisposable
{
    public void Dispose()
    {
        AiModels.ModelOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        admin.DeleteAsync("/api/settings/Bitween.Ai.ApiKey").GetAwaiter().GetResult();
    }

    sealed class FakeModel(Func<AiPrompt, string> answer) : IAiModel
    {
        public List<AiPrompt> Asked { get; } = new();

        public Task<string> Complete(AiPrompt prompt)
        {
            Asked.Add(prompt);
            return Task.FromResult(answer(prompt));
        }
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
    public async Task A_question_becomes_the_pages_filter_from_names_alone()
    {
        using var admin = await fixture.AdminAsync();
        var partnerName = Api.Unique("SkyNet");
        var partnerId = await Api.CreateAsync(admin, "/api/partners", new { name = partnerName });
        await ConfigureAsync(admin);
        var model = new FakeModel(_ => $$"""{ "status": "failed", "partnerIds": [{{partnerId}}, 2147483000], "range": "7d", "answer": "{count} failed" }""");
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/xchangequeries",
            new { question = "failed skynet this week", timeZone = "Asia/Amman" }));

        Assert.Equal($"status=failed&partnerId={partnerId}&range=7d", (string)result["query"]!);
        Assert.Equal("{count} failed", (string)result["answer"]!);
        Assert.Contains("There is no partner with id 2147483000.", result["notes"]!.AsArray().Select(n => (string)n!));
        var sent = Assert.Single(model.Asked).Message;
        Assert.Equal(sent, (string)result["sent"]!);
        Assert.Contains($"{partnerId}: {partnerName}", sent);
        Assert.Contains("in Asia/Amman.", sent);

        var audited = await fixture.InDbAsync(db => db.Set<OperatorAction>().AsNoTracking()
            .AnyAsync(a => a.Action == OperatorAction.AskExchanges && a.Detail.EndsWith("failed skynet this week")));
        Assert.True(audited, "the question wasn't recorded");
    }

    [Fact]
    public async Task Without_AI_set_up_nothing_is_sent()
    {
        using var admin = await fixture.AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/xchangequeries", new { question = "failed today" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Settings, AI", await response.Content.ReadAsStringAsync());
    }
}
