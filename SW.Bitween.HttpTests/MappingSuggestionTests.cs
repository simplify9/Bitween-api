using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Services.Ai;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Generate with AI: the model under Settings, AI writes the rules, and the server checks them
/// against the samples before the editor gets them.
/// </summary>
[Collection("Http")]
public class MappingSuggestionTests(HttpFixture fixture) : IDisposable
{
    public void Dispose()
    {
        AiModels.ModelOverride = null;
        AiModels.HandlerOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        foreach (var key in new[] { "Bitween.Ai.Provider", "Bitween.Ai.ApiKey", "Bitween.Ai.Model", "Bitween.Ai.BaseUrl" })
            admin.DeleteAsync($"/api/settings/{key}").GetAwaiter().GetResult();
    }

    const string Source = """{ "order": { "id": "SO-1", "customer": "acme", "lines": [ { "sku": "A1", "qty": "2" } ] } }""";
    const string Target = """{ "reference": "SO-1", "customer": "ACME", "items": [ { "sku": "A1", "quantity": 2 } ] }""";

    const string Right = """
        { "version": 1,
          "fields": [ { "target": ["reference"], "from": { "kind": "path", "path": "order.id" } },
                      { "target": ["customer"], "from": { "kind": "path", "path": "order.customer" }, "transform": { "fn": "upper" } } ],
          "lists": [ { "over": "order.lines", "target": ["items"], "fields": [
                      { "target": ["sku"], "from": { "kind": "path", "path": "sku" } },
                      { "target": ["quantity"], "from": { "kind": "path", "path": "qty" }, "type": "number" } ] } ] }
        """;

    /// <summary>Right but for the quantity, which stays text.</summary>
    static readonly string AlmostRight = Right.Replace(""", "type": "number" """, " ");

    sealed class FakeModel(Func<AiPrompt, string> answer) : IAiModel
    {
        public List<AiPrompt> Asked { get; } = new();

        public Task<string> Complete(AiPrompt prompt)
        {
            Asked.Add(prompt);
            return Task.FromResult(answer(prompt));
        }
    }

    sealed class FakeProvider(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Received { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Received.Add((request, body));
            return answer(request, body);
        }
    }

    async Task<HttpClient> ConfiguredAsync(params (string Key, string Value)[] settings)
    {
        var admin = await fixture.AdminAsync();
        var all = new[] { ("Bitween.Ai.ApiKey", "sk-test-key") }.Concat(settings).ToArray();
        foreach (var (key, value) in all)
            await Api.Json(await admin.PostAsJsonAsync($"/api/settings/{key}", new { value }));

        // Each save also broadcasts a reload, which this node handles a moment later; one started by
        // an earlier save can read the table before a later save lands and briefly put the earlier
        // values back. Wait until what was saved is what's running, as it is by the time anyone
        // clicks Generate after saving Settings.
        var live = fixture.App.Services.GetRequiredService<BitweenOptions>();
        for (var waited = 0; !all.All(s => Live(live, s.Item1) == s.Item2); waited += 50)
        {
            Assert.True(waited < 10_000, "the saved AI settings never took effect");
            await Task.Delay(50);
        }
        return admin;
    }

    static string? Live(BitweenOptions options, string key) => key switch
    {
        "Bitween.Ai.ApiKey" => options.Ai.ApiKey,
        "Bitween.Ai.Provider" => options.Ai.Provider,
        "Bitween.Ai.Model" => options.Ai.Model,
        "Bitween.Ai.BaseUrl" => options.Ai.BaseUrl,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    static Task<HttpResponseMessage> SuggestAsync(HttpClient client, bool refine = false, string rules = """{ "version": 1 }""") =>
        client.PostAsJsonAsync("/api/mappingsuggestions",
            new { mappingRules = rules, sourceDocument = Source, targetDocument = Target, refine });

    static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Without_a_key_it_says_where_to_set_one()
    {
        var admin = await fixture.AdminAsync();

        var response = await SuggestAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Settings, AI", await response.Content.ReadAsStringAsync());
        var config = await Api.Json(await fixture.Client().GetAsync("/api/settings/config"));
        Assert.False((bool)config["aiConfigured"]!);
    }

    [Fact]
    public async Task Rules_that_produce_the_output_sample_come_back_checked()
    {
        var admin = await ConfiguredAsync();
        var model = new FakeModel(_ => $"```json\n{Right}\n```");
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await SuggestAsync(admin));

        Assert.True((bool)result["matchesTarget"]!, result.ToJsonString());
        Assert.Empty(result["differences"]!.AsArray());
        Assert.Contains("\"kind\":\"path\"", (string)result["mappingRules"]!);
        // Both samples went to the model, after the same standing instructions every call gets.
        var asked = Assert.Single(model.Asked);
        Assert.Equal(MappingRulesPrompt.Instructions, asked.Instructions);
        Assert.Contains("SO-1", asked.Message);
        Assert.Contains("\"quantity\": 2", asked.Message);
        Assert.True(asked.Json);
        var config = await Api.Json(await fixture.Client().GetAsync("/api/settings/config"));
        Assert.True((bool)config["aiConfigured"]!);
    }

    [Fact]
    public async Task Differences_are_reported_and_a_refinement_fixes_them()
    {
        var admin = await ConfiguredAsync();
        var model = new FakeModel(prompt => prompt.Message.Contains("did not produce") ? Right : AlmostRight);
        AiModels.ModelOverride = _ => model;

        var first = await Api.Json(await SuggestAsync(admin));
        Assert.False((bool)first["matchesTarget"]!);
        var difference = first["differences"]!.AsArray().Single()!;
        Assert.Equal("items[0].quantity", (string)difference["target"]!);
        Assert.Equal("the output sample has the number 2, the rules write the text \"2\"", (string)difference["reason"]!);

        // The editor sends back what it was given; the model sees those rules and what was wrong with them.
        var second = await Api.Json(await SuggestAsync(admin, refine: true, rules: (string)first["mappingRules"]!));

        Assert.True((bool)second["matchesTarget"]!, second.ToJsonString());
        Assert.Contains("items[0].quantity", model.Asked[1].Message);
    }

    /// <summary>
    /// The model may map a partner or global value it is told the name of; the value itself stays
    /// here, filled in by the check's preview as it is when the mapping runs.
    /// </summary>
    [Fact]
    public async Task Partner_and_global_values_reach_the_model_by_name_only()
    {
        var admin = await ConfiguredAsync();
        var setId = Api.Unique("depots").Replace(" ", "-");
        await Api.Json(await admin.PostAsJsonAsync("/api/globaladaptervaluessets", new
        {
            id = setId, name = setId,
            values = new Dictionary<string, string> { ["main"] = "DEPOT-AMM-9", ["vault"] = "do-not-send-1" },
            secretProperties = new[] { "vault" },
        }));
        var partnerId = await Api.CreateAsync(admin, "/api/partners", new
        {
            name = Api.Unique("AI partner"),
            adapterProperties = new Dictionary<string, string> { ["SenderId"] = "SENDER-77" },
        });
        var model = new FakeModel(_ => $$"""
            { "version": 1, "fields": [
              { "target": ["sender"], "from": { "kind": "partner", "key": "SenderId" } },
              { "target": ["depot"], "from": { "kind": "global", "setId": "{{setId}}", "key": "main" } } ] }
            """);
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/mappingsuggestions", new
        {
            mappingRules = """{ "version": 1 }""",
            sourceDocument = """{ "order": 1 }""",
            targetDocument = """{ "sender": "SENDER-77", "depot": "DEPOT-AMM-9" }""",
            partnerId,
        }));

        Assert.True((bool)result["matchesTarget"]!, result.ToJsonString());
        var asked = Assert.Single(model.Asked).Message;
        Assert.Contains("- partner properties: SenderId", asked);
        Assert.Contains($"- global value set \"{setId}\", keys: main", asked);
        // Only the output sample the person pasted carries those values; nothing Bitween holds does.
        var withoutSamples = asked[asked.IndexOf("</output_sample>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("SENDER-77", withoutSamples);
        Assert.DoesNotContain("DEPOT-AMM-9", withoutSamples);
        Assert.DoesNotContain("vault", asked);
        Assert.DoesNotContain("do-not-send-1", asked);
    }

    [Fact]
    public async Task Instructions_reach_the_model_and_the_answer_names_its_model()
    {
        var admin = await ConfiguredAsync(("Bitween.Ai.Model", "claude-sonnet-5-5"));
        var model = new FakeModel(_ => Right);
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/mappingsuggestions", new
        {
            mappingRules = """{ "version": 1 }""", sourceDocument = Source, targetDocument = Target,
            instructions = "Quantities are always whole numbers.",
        }));

        Assert.Equal("claude-sonnet-5-5", (string)result["model"]!);
        Assert.Contains("<instructions>\nQuantities are always whole numbers.".Replace("\n", Environment.NewLine), model.Asked.Single().Message);
    }

    [Fact]
    public async Task An_unreadable_answer_is_asked_for_once_more_with_the_reason()
    {
        var admin = await ConfiguredAsync();
        var model = new FakeModel(prompt => prompt.Message.Contains("could not be used")
            ? Right
            : """{ "version": 1, "fields": [ { "target": ["reference"], "from": { "kind": "path", "path": "order.id" }, "when": "always" } ] }""");
        AiModels.ModelOverride = _ => model;

        var result = await Api.Json(await SuggestAsync(admin));

        Assert.True((bool)result["matchesTarget"]!);
        Assert.Equal(2, model.Asked.Count);
        Assert.Contains("when", model.Asked[1].Message);
    }

    [Fact]
    public async Task A_sample_that_isnt_valid_is_refused_before_anything_is_asked()
    {
        var admin = await ConfiguredAsync();
        var model = new FakeModel(_ => Right);
        AiModels.ModelOverride = _ => model;

        var response = await admin.PostAsJsonAsync("/api/mappingsuggestions",
            new { mappingRules = """{ "version": 1 }""", sourceDocument = "{ not json", targetDocument = Target });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("source sample", await response.Content.ReadAsStringAsync());
        Assert.Empty(model.Asked);
    }

    [Fact]
    public async Task Anthropic_is_called_with_the_key_the_default_model_and_cached_instructions()
    {
        var admin = await ConfiguredAsync();
        var provider = new FakeProvider((_, _) => Ok(new
        {
            id = "msg_1", type = "message", role = "assistant", model = "claude-opus-5-5",
            content = new[] { new { type = "text", text = Right } },
            stop_reason = "end_turn", stop_sequence = (string?)null,
            usage = new { input_tokens = 10, output_tokens = 10 },
        }));
        AiModels.HandlerOverride = () => provider;

        var result = await Api.Json(await SuggestAsync(admin));

        Assert.True((bool)result["matchesTarget"]!, result.ToJsonString());
        var (request, body) = Assert.Single(provider.Received);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test-key", request.Headers.GetValues("x-api-key").Single());
        var sent = JsonNode.Parse(body)!;
        Assert.Equal("claude-opus-5-5", (string)sent["model"]!);
        Assert.Equal("ephemeral", (string)sent["system"]![0]!["cache_control"]!["type"]!);
    }

    [Fact]
    public async Task A_key_Anthropic_refuses_is_reported_as_the_keys_fault()
    {
        var admin = await ConfiguredAsync();
        AiModels.HandlerOverride = () => new FakeProvider((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""",
                Encoding.UTF8, "application/json"),
        });

        var response = await SuggestAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("API key under Settings, AI isn't valid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_OpenAI_compatible_provider_is_called_at_its_own_address_for_JSON()
    {
        var admin = await ConfiguredAsync(("Bitween.Ai.Provider", "OpenAI"), ("Bitween.Ai.Model", "local-model"),
            ("Bitween.Ai.BaseUrl", "http://ollama.test:11434/v1/"));
        var provider = new FakeProvider((_, _) => Ok(new
        {
            id = "chatcmpl-1", @object = "chat.completion", created = 1760000000, model = "local-model",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = Right }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 10, completion_tokens = 10, total_tokens = 20 },
        }));
        AiModels.HandlerOverride = () => provider;

        var result = await Api.Json(await SuggestAsync(admin));

        Assert.True((bool)result["matchesTarget"]!, result.ToJsonString());
        var (request, body) = Assert.Single(provider.Received);
        Assert.Equal("http://ollama.test:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test-key", request.Headers.Authorization!.ToString());
        var sent = JsonNode.Parse(body)!;
        Assert.Equal("local-model", (string)sent["model"]!);
        Assert.Equal("json_object", (string)sent["response_format"]!["type"]!);
    }
}
