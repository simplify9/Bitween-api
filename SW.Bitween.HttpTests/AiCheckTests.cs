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
/// Settings, AI checks what's on screen before it's saved: a small call to the model, and the
/// provider's model list to choose from.
/// </summary>
[Collection("Http")]
public class AiCheckTests(HttpFixture fixture) : IDisposable
{
    public void Dispose()
    {
        AiModels.HandlerOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        admin.DeleteAsync("/api/settings/Bitween.Ai.ApiKey").GetAwaiter().GetResult();
    }

    sealed class FakeProvider(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Received { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Received.Add((request, body));
            return answer(request);
        }
    }

    static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    static HttpResponseMessage Chat(string content) => Ok(new
    {
        id = "chatcmpl-1", @object = "chat.completion", created = 1760000000, model = "cheap-model",
        choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
        usage = new { prompt_tokens = 10, completion_tokens = 10, total_tokens = 20 },
    });

    static HttpResponseMessage ModelList(params string[] ids) =>
        Ok(new { @object = "list", data = ids.Select(id => new { id, @object = "model", created = 1760000000, owned_by = "x" }) });

    FakeProvider Answering(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var provider = new FakeProvider(answer);
        AiModels.HandlerOverride = () => provider;
        return provider;
    }

    /// <summary>Saves an Anthropic key, waiting until it's the one running.</summary>
    async Task<HttpClient> WithSavedKeyAsync()
    {
        var admin = await fixture.AdminAsync();
        await Api.Json(await admin.PostAsJsonAsync("/api/settings/Bitween.Ai.ApiKey", new { value = "sk-saved" }));
        var live = fixture.App.Services.GetRequiredService<BitweenOptions>();
        for (int waited = 0, steady = 0; steady < 300; waited += 50)
        {
            Assert.True(waited < 10_000, "the saved key never took effect");
            await Task.Delay(50);
            steady = live.Ai.ApiKey == "sk-saved" ? steady + 50 : 0;
        }
        return admin;
    }

    [Fact]
    public async Task A_check_calls_the_model_on_screen_with_the_key_typed_and_asks_for_JSON()
    {
        var admin = await fixture.AdminAsync();
        var provider = Answering(_ => Chat("""{"ok": true}"""));

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/aichecks",
            new { provider = "Google Gemini", apiKey = "typed-key", model = "cheap-model" }));

        Assert.Equal("cheap-model", (string)result["model"]!);
        var (request, body) = Assert.Single(provider.Received);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer typed-key", request.Headers.Authorization!.ToString());
        Assert.Equal("json_object", (string)JsonNode.Parse(body)!["response_format"]!["type"]!);
    }

    [Fact]
    public async Task A_model_that_answers_without_JSON_fails_the_check()
    {
        var admin = await fixture.AdminAsync();
        Answering(_ => Chat("Sure! Everything is fine."));

        var response = await admin.PostAsJsonAsync("/api/aichecks", new { provider = "Groq", apiKey = "typed-key", model = "cheap-model" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not with JSON", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_provider_that_cant_be_reached_is_said_so_plainly()
    {
        var admin = await fixture.AdminAsync();
        Answering(_ => throw new HttpRequestException("Connection refused"));

        var response = await admin.PostAsJsonAsync("/api/aichecks", new { provider = "Ollama", model = "llama-local" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Ollama can't be reached right now", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_saved_key_is_used_for_the_provider_it_was_saved_for()
    {
        var admin = await WithSavedKeyAsync();
        var provider = Answering(_ => Ok(new
        {
            id = "msg_1", type = "message", role = "assistant", model = "claude-haiku-5-5",
            content = new[] { new { type = "text", text = """{"ok": true}""" } },
            stop_reason = "end_turn", stop_sequence = (string?)null,
            usage = new { input_tokens = 10, output_tokens = 10 },
        }));

        await Api.Json(await admin.PostAsJsonAsync("/api/aichecks", new { provider = "Anthropic", model = "claude-haiku-5-5" }));

        var (request, _) = Assert.Single(provider.Received);
        Assert.Equal("sk-saved", request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task The_saved_key_is_never_sent_to_another_provider_or_address()
    {
        var admin = await WithSavedKeyAsync();
        var provider = Answering(_ => Chat("""{"ok": true}"""));

        var otherProvider = await admin.PostAsJsonAsync("/api/aichecks", new { provider = "DeepSeek", model = "cheap-model" });
        var otherAddress = await admin.PostAsJsonAsync("/api/aichecks/models",
            new { provider = "Anthropic", baseUrl = "https://proxy.example.com/" });

        Assert.Equal(HttpStatusCode.BadRequest, otherProvider.StatusCode);
        Assert.Contains("Type the API key for DeepSeek", await otherProvider.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, otherAddress.StatusCode);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task Models_are_the_providers_chat_models_sorted_by_name()
    {
        var admin = await fixture.AdminAsync();
        var provider = Answering(_ => ModelList("models/gemini-pro", "text-embedding-4", "models/gemini-flash", "whisper-large", "models/gemini-flash-image"));

        var result = await Api.Json(await admin.PostAsJsonAsync("/api/aichecks/models", new { provider = "Google Gemini", apiKey = "typed-key" }));

        Assert.Equal(new[] { "gemini-flash", "gemini-pro" }, result["models"]!.AsArray().Select(m => (string)m!).ToArray());
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/models", Assert.Single(provider.Received).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Bedrock_is_called_in_the_region_typed_and_named_as_itself()
    {
        var admin = await fixture.AdminAsync();
        var provider = Answering(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await admin.PostAsJsonAsync("/api/aichecks", new
        {
            provider = "Amazon Bedrock", apiKey = "typed-key", model = "anthropic.claude-model",
            baseUrl = "https://bedrock-runtime.eu-central-1.amazonaws.com/openai/v1/",
        });

        Assert.Contains("Amazon Bedrock refused the request", await response.Content.ReadAsStringAsync());
        Assert.Equal("https://bedrock-runtime.eu-central-1.amazonaws.com/openai/v1/chat/completions",
            Assert.Single(provider.Received).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Bedrock_without_a_region_says_the_region_is_missing()
    {
        var admin = await fixture.AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/aichecks", new { provider = "Amazon Bedrock", apiKey = "typed-key", model = "m" });

        Assert.Contains("until the AWS region is set", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_providers_say_how_their_address_is_made()
    {
        var admin = await fixture.AdminAsync();

        var providers = (await Api.Json(await admin.GetAsync("/api/aichecks/providers"))).AsArray();

        var bedrock = providers.Single(p => (string)p!["name"]! == "Amazon Bedrock")!;
        Assert.Equal("AWS region", (string)bedrock["part"]!);
        Assert.False((bool)bedrock["lists"]!);
        var qwen = providers.Single(p => (string)p!["name"]! == "Alibaba Cloud (Qwen)")!;
        Assert.Equal(3, qwen["places"]!.AsArray().Count);
        Assert.True((bool)providers.Single(p => (string)p!["name"]! == "Other (OpenAI-compatible)")!["keyless"]!);
    }

    [Fact]
    public async Task Ollama_lists_without_a_key_and_Azure_lists_nothing()
    {
        var admin = await fixture.AdminAsync();
        var provider = Answering(_ => ModelList("llama-local"));

        var ollama = await Api.Json(await admin.PostAsJsonAsync("/api/aichecks/models", new { provider = "Ollama" }));
        var azure = await Api.Json(await admin.PostAsJsonAsync("/api/aichecks/models",
            new { provider = "Azure OpenAI", apiKey = "typed-key", baseUrl = "https://acme.openai.azure.com/openai/v1/" }));

        Assert.Equal("llama-local", (string)ollama["models"]![0]!);
        Assert.Null(azure["models"]);
        Assert.Equal("http://localhost:11434/v1/models", Assert.Single(provider.Received).Request.RequestUri!.ToString());
    }
}
