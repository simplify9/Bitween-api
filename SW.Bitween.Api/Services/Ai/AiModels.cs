using System;
using System.Net.Http;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Ai;

/// <summary>One request to a model: standing instructions, one message, and room for the answer.</summary>
/// <param name="Instructions">The same on every call of a feature, so a provider can cache it.</param>
/// <param name="Message">What this call is about.</param>
/// <param name="Json">Whether the answer must be a single JSON object, for providers that can enforce it.</param>
public record AiPrompt(string Instructions, string Message, bool Json = false, int MaxTokens = 16000);

/// <summary>A configured model, as an AI feature sees it: text in, text out.</summary>
public interface IAiModel
{
    /// <summary>The model's answer.</summary>
    /// <exception cref="SWValidationException">When the provider can't be reached, refuses, or cuts the answer off.</exception>
    Task<string> Complete(AiPrompt prompt);
}

/// <summary>
/// The model Settings, AI names, built fresh for each call so a changed key or model applies at once.
/// </summary>
/// <remarks>
/// Each provider gets its own official SDK behind <see cref="IAiModel"/>, rather than one SDK
/// pretending to be the other: their requests differ in ways a shim hides - Anthropic caches the
/// instructions, OpenAI enforces a JSON answer.
/// </remarks>
public class AiModels(BitweenOptions options)
{
    // One client for the process, recycling its connections, as RegistryClient does. The SDKs set
    // their own per-request timeout, so this one never cuts them short.
    static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>How long one call may take. Long enough for a model to think about a large document.</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(3);

    /// <summary>For tests: answers in place of the network, as <c>RegistryClient.HandlerOverride</c> does.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride { get; set; }

    /// <summary>For tests: a model in place of either provider, for features tested without HTTP.</summary>
    internal static Func<AiOptions, IAiModel> ModelOverride { get; set; }

    static HttpClient Client => HandlerOverride is { } handler ? new HttpClient(handler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan } : Http;

    public bool Configured => options.Ai.IsSet;

    /// <summary>The model <see cref="Current"/> calls, as Settings names it.</summary>
    public string ModelName => options.Ai.ResolvedModel;

    /// <exception cref="SWValidationException"><c>AI_NOT_CONFIGURED</c> until Settings, AI has a key.</exception>
    public IAiModel Current()
    {
        var ai = options.Ai;
        if (!ai.IsSet)
            throw new SWValidationException("AI_NOT_CONFIGURED",
                string.IsNullOrWhiteSpace(ai.ApiKey)
                    ? "AI features are off until an API key is set under Settings, AI."
                    : "AI features are off until a model is named under Settings, AI.");

        if (ModelOverride is { } model) return model(ai);

        var baseUrl = string.IsNullOrWhiteSpace(ai.BaseUrl) ? null : ai.BaseUrl.Trim();
        return ai.IsAnthropic
            ? new AnthropicModel(ai.ApiKey.Trim(), ai.ResolvedModel, baseUrl, Client)
            : new OpenAiModel(ai.ApiKey.Trim(), ai.ResolvedModel, baseUrl, Client);
    }

    internal static SWValidationException Unreachable(string provider, string detail) =>
        new("AI_UNREACHABLE", $"{provider} can't be reached right now: {detail}");

    internal static SWValidationException Refused(string provider, string detail) =>
        new("AI_PROVIDER_ERROR", $"{provider} refused the request: {detail}");
}
