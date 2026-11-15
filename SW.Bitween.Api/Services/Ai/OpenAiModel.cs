using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Ai;

/// <summary>OpenAI's chat completions, and every provider that speaks them at its own address.</summary>
/// <remarks>
/// No output-token limit is sent: compatible servers disagree about what the parameter is called,
/// and each one's own limit is generous. A cut-off answer still shows, as a finish reason.
/// </remarks>
sealed class OpenAiModel(string name, string apiKey, string model, string baseUrl, HttpClient http) : IAiModel
{
    public async Task<string> Complete(AiPrompt prompt)
    {
        var client = new ChatClient(model, new ApiKeyCredential(apiKey), Options(baseUrl, http));
        var chatOptions = new ChatCompletionOptions();
        if (prompt.Json) chatOptions.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        var completion = await Call(name, model, async () => (await client.CompleteChatAsync(
            [new SystemChatMessage(prompt.Instructions), new UserChatMessage(prompt.Message)],
            chatOptions)).Value);

        if (completion.FinishReason == ChatFinishReason.ContentFilter)
            throw new SWValidationException("AI_REFUSED", "The provider's content filter declined this request.");

        if (completion.FinishReason == ChatFinishReason.Length)
            throw new SWValidationException("AI_TRUNCATED",
                "The model's answer was too long and was cut off. Try smaller samples.");

        return string.Concat(completion.Content.Select(part => part.Text));
    }

    /// <summary>The names of the models the provider lists for this key.</summary>
    public static async Task<List<string>> List(string name, string apiKey, string baseUrl, HttpClient http)
    {
        var client = new OpenAIModelClient(new ApiKeyCredential(apiKey), Options(baseUrl, http));
        var models = await Call(name, null, async () => (await client.GetModelsAsync()).Value);
        return models.Select(m => m.Id).ToList();
    }

    static OpenAIClientOptions Options(string baseUrl, HttpClient http)
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http),
            NetworkTimeout = AiModels.CallTimeout,
        };
        if (baseUrl != null) options.Endpoint = new Uri(baseUrl);
        return options;
    }

    /// <param name="model">Null when listing models, where a 404 means the address rather than a model.</param>
    static async Task<T> Call<T>(string name, string model, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            throw AiModels.Refused(name, "the API key under Settings, AI isn't valid.");
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw AiModels.Refused(name, model is null
                ? "nothing at this address lists models. Check the API address under Settings, AI."
                : $"there's no model called '{model}' at this address. Check Settings, AI.");
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw AiModels.Unreachable(name, "the account's rate limit was reached. Try again in a minute.");
        }
        catch (ClientResultException ex) when (ex.Status == 0)
        {
            // No response arrived at all.
            throw AiModels.Unreachable(name, AiModels.NoConnection);
        }
        catch (ClientResultException ex) when (ex.Status >= 500)
        {
            throw AiModels.Unreachable(name, AiModels.Answered(ex.Status));
        }
        catch (ClientResultException ex)
        {
            throw AiModels.Refused(name, AiModels.Answered(ex.Status));
        }
        // The SDK retries a failed connection, then throws the attempts' failures together.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or AggregateException)
        {
            throw AiModels.Unreachable(name, AiModels.NoConnection);
        }
    }
}
