using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Ai;

/// <summary>OpenAI's chat completions, and anything that speaks them at another address.</summary>
/// <remarks>
/// No output-token limit is sent: compatible servers disagree about what the parameter is called,
/// and each one's own limit is generous. A cut-off answer still shows, as a finish reason.
/// </remarks>
sealed class OpenAiModel(string apiKey, string model, string baseUrl, HttpClient http) : IAiModel
{
    string Name => baseUrl == null ? "OpenAI" : $"The AI provider at {baseUrl}";

    public async Task<string> Complete(AiPrompt prompt)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http),
            NetworkTimeout = AiModels.CallTimeout,
        };
        if (baseUrl != null) clientOptions.Endpoint = new Uri(baseUrl);

        var client = new ChatClient(model, new ApiKeyCredential(apiKey), clientOptions);
        var chatOptions = new ChatCompletionOptions();
        if (prompt.Json) chatOptions.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        ChatCompletion completion;
        try
        {
            completion = (await client.CompleteChatAsync(
                [new SystemChatMessage(prompt.Instructions), new UserChatMessage(prompt.Message)],
                chatOptions)).Value;
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            throw AiModels.Refused(Name, "the API key under Settings, AI isn't valid.");
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw AiModels.Refused(Name, $"there's no model called '{model}' at this address. Check Settings, AI.");
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw AiModels.Unreachable(Name, "the account's rate limit was reached. Try again in a minute.");
        }
        catch (ClientResultException ex) when (ex.Status >= 500 || ex.Status == 0)
        {
            // 0: no response arrived at all.
            throw AiModels.Unreachable(Name, ex.Message);
        }
        catch (ClientResultException ex)
        {
            throw AiModels.Refused(Name, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            throw AiModels.Unreachable(Name, ex.Message);
        }

        if (completion.FinishReason == ChatFinishReason.ContentFilter)
            throw new SWValidationException("AI_REFUSED", "The provider's content filter declined this request.");

        if (completion.FinishReason == ChatFinishReason.Length)
            throw new SWValidationException("AI_TRUNCATED",
                "The model's answer was too long and was cut off. Try smaller samples.");

        return string.Concat(completion.Content.Select(part => part.Text));
    }
}
