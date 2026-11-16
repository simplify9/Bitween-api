using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Anthropic.Models.Models;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Ai;

/// <summary>Claude, through Anthropic's own SDK.</summary>
/// <remarks>
/// <para>
/// Neither thinking nor effort is set: the model is whatever Settings names, and the models disagree
/// about both - an older one rejects an effort it doesn't know. Each current model's own default
/// applies instead, which for the default, Opus 5.5, is adaptive thinking at medium effort.
/// </para>
/// <para>
/// The instructions are marked for caching: they're the same on every call of a feature, and the
/// larger part of each request.
/// </para>
/// </remarks>
sealed class AnthropicModel(string apiKey, string model, string baseUrl, HttpClient http) : IAiModel
{
    const string Name = "Anthropic";

    public async Task<string> Complete(AiPrompt prompt)
    {
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = prompt.MaxTokens,
            System = new List<TextBlockParam>
            {
                new() { Text = prompt.Instructions, CacheControl = new CacheControlEphemeral() },
            },
            Messages = [new() { Role = Role.User, Content = prompt.Message }],
        };

        var response = await Call(model, () => Client(apiKey, baseUrl, http).Messages.Create(parameters));

        if (response.StopReason == "refusal")
            throw new SWValidationException("AI_REFUSED",
                $"The model declined this request{(response.StopDetails?.Explanation is { } why ? ": " + why : ".")}");

        if (response.StopReason == "max_tokens")
            throw new SWValidationException("AI_TRUNCATED",
                "The model's answer was too long and was cut off. Try smaller samples.");

        return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    /// <summary>The names of the models this key can use.</summary>
    public static async Task<List<string>> List(string apiKey, string baseUrl, HttpClient http)
    {
        var page = await Call(null, () => Client(apiKey, baseUrl, http).Models.List(new ModelListParams { Limit = 1000 }));
        return page.Items.Select(m => m.ID).ToList();
    }

    static AnthropicClient Client(string apiKey, string baseUrl, HttpClient http) => baseUrl == null
        ? new AnthropicClient { ApiKey = apiKey, HttpClient = http, Timeout = AiModels.CallTimeout }
        : new AnthropicClient { ApiKey = apiKey, HttpClient = http, Timeout = AiModels.CallTimeout, BaseUrl = baseUrl };

    /// <param name="model">Null when listing models, where a 404 means the address rather than a model.</param>
    static async Task<T> Call<T>(string model, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (AnthropicUnauthorizedException)
        {
            throw AiModels.Refused(Name, "the API key under Settings, AI isn't valid.");
        }
        catch (AnthropicNotFoundException)
        {
            throw AiModels.Refused(Name, model is null
                ? "nothing at this address lists models. Check the API address under Settings, AI."
                : $"there's no model called '{model}'. Check Settings, AI.");
        }
        catch (AnthropicRateLimitException)
        {
            throw AiModels.Unreachable(Name, "the account's rate limit was reached. Try again in a minute.");
        }
        catch (Anthropic5xxException ex)
        {
            throw AiModels.Unreachable(Name, AiModels.Answered((int)ex.StatusCode));
        }
        catch (AnthropicApiException ex)
        {
            throw AiModels.Refused(Name, AiModels.Answered((int)ex.StatusCode));
        }
        catch (Exception ex) when (ex is AnthropicIOException or HttpRequestException or TaskCanceledException)
        {
            throw AiModels.Unreachable(Name, AiModels.NoConnection);
        }
    }
}
