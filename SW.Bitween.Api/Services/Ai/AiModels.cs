using System;
using System.Collections.Generic;
using System.Linq;
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
    // One client per address policy for the process, recycling its connections, as RegistryClient
    // does. The SDKs set their own per-request timeout, so this one never cuts them short.
    //
    // Connections go through the adapters' address guard: the API address is an administrator's
    // setting, and an AI call carries the key and the samples, so it gets no more reach than an
    // HTTP adapter has - never link-local or cloud metadata, and not private or loopback either
    // while Bitween:BlockPrivateNetworkAddresses is on.
    static readonly Lazy<HttpClient> Open = new(() => Guarded(blockPrivate: false));
    static readonly Lazy<HttpClient> Closed = new(() => Guarded(blockPrivate: true));

    static HttpClient Guarded(bool blockPrivate) => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = SW.Bitween.NativeAdapters.OutboundAddressGuard.ConnectCallback(blockPrivate),
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>How long one call may take. Long enough for a model to think about a large document.</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(3);

    /// <summary>For tests: answers in place of the network, as <c>RegistryClient.HandlerOverride</c> does.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride { get; set; }

    /// <summary>For tests: a model in place of either provider, for features tested without HTTP.</summary>
    internal static Func<AiOptions, IAiModel> ModelOverride { get; set; }

    HttpClient Client => HandlerOverride is { } handler
        ? new HttpClient(handler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }
        : options.BlockPrivateNetworkAddresses ? Closed.Value : Open.Value;

    public bool Configured => options.Ai.IsSet;

    /// <summary>The model <see cref="Current"/> calls, as Settings names it.</summary>
    public string ModelName => options.Ai.ResolvedModel;

    /// <exception cref="SWValidationException"><c>AI_NOT_CONFIGURED</c> until Settings, AI has a key.</exception>
    public IAiModel Current() => For(options.Ai);

    /// <summary>A model for these settings - the saved ones, or ones Settings is about to save.</summary>
    /// <exception cref="SWValidationException"><c>AI_NOT_CONFIGURED</c> while they're incomplete.</exception>
    public IAiModel For(AiOptions ai)
    {
        if (!ai.IsSet)
            throw new SWValidationException("AI_NOT_CONFIGURED",
                ai.Known is null ? $"'{ai.Provider}' isn't an AI provider Bitween knows."
                : ai.NeedsKey && string.IsNullOrWhiteSpace(ai.ApiKey) ? "AI features are off until an API key is set under Settings, AI."
                : ai.ResolvedModel is null ? "AI features are off until a model is named under Settings, AI."
                : $"AI features are off until the {ai.Info.Part ?? "API address"} is set under Settings, AI.");
        CheckAddress(ai);

        if (ModelOverride is { } model) return model(ai);

        return ai.IsAnthropic
            ? new AnthropicModel(ai.ApiKey.Trim(), ai.ResolvedModel, ai.ResolvedBaseUrl, Client)
            : new OpenAiModel(NameOf(ai), KeyOf(ai), ai.ResolvedModel, ai.ResolvedBaseUrl, Client);
    }

    /// <summary>
    /// The chat models these settings' provider lists, for Settings to offer. Null for a provider
    /// that doesn't list them, such as Azure OpenAI, whose deployments only its portal names.
    /// </summary>
    public async Task<List<string>> Models(AiOptions ai)
    {
        if (ai.Known is null)
            throw new SWValidationException("AI_NOT_CONFIGURED", $"'{ai.Provider}' isn't an AI provider Bitween knows.");
        if (!ai.Info.Lists) return null;
        if (ai.NeedsKey && string.IsNullOrWhiteSpace(ai.ApiKey))
            throw new SWValidationException("AI_NOT_CONFIGURED", "Set the API key to see this provider's models.");
        if (ai.ResolvedBaseUrl is null)
            throw new SWValidationException("AI_NOT_CONFIGURED", "Set the API address to see this provider's models.");
        CheckAddress(ai);

        var listed = ai.IsAnthropic
            ? await AnthropicModel.List(ai.ApiKey.Trim(), ai.ResolvedBaseUrl, Client)
            : await OpenAiModel.List(NameOf(ai), KeyOf(ai), ai.ResolvedBaseUrl, Client);

        var gemini = ai.Known == "Google Gemini";
        return listed
            // Gemini lists "models/gemini-…" but is called with the bare name; other names stay as listed.
            .Select(id => gemini && id.StartsWith("models/", StringComparison.Ordinal) ? id["models/".Length..] : id)
            .Where(id => !NotChat.Any(word => id.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Words in the names of models that can't answer a chat - embeddings, speech, images - which
    /// the providers list beside the ones that can. A model this misses can still be typed in.
    /// </summary>
    static readonly string[] NotChat =
        ["embed", "whisper", "tts", "dall-e", "image", "imagen", "veo", "sora", "audio", "speech", "transcribe",
         "realtime", "moderation", "rerank", "ocr", "babbage", "davinci", "guard"];

    /// <summary>
    /// Sends these settings' model a small request shaped like the real ones - instructions, a
    /// message, a JSON answer - so a wrong key, model or address shows in Settings, not later in a
    /// mapping. Returns how long it took.
    /// </summary>
    public async Task<TimeSpan> Check(AiOptions ai)
    {
        var model = For(ai);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var answer = await model.Complete(new AiPrompt(
            "You check that a connection works. Answer with one JSON object and nothing else.",
            """Answer with exactly this JSON object: {"ok": true}""",
            Json: true, MaxTokens: 2000));

        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        try
        {
            if (start < 0 || end < start) throw new System.Text.Json.JsonException();
            using var _ = System.Text.Json.JsonDocument.Parse(answer[start..(end + 1)]);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new SWValidationException("AI_NOT_JSON",
                "The model answered, but not with JSON. AI features need a model that can; choose a larger one.");
        }
        return started.Elapsed;
    }

    static void CheckAddress(AiOptions ai)
    {
        // Checked here as well as when the setting is saved: configuration binds it without the check.
        if (AiOptions.AddressProblem(ai.BaseUrl) is { } problem)
            throw new SWValidationException("AI_NOT_CONFIGURED", $"The API address under Settings, AI can't be used: {problem}");
    }

    /// <summary>Named after the provider while it's at one of its own addresses; after the address otherwise.</summary>
    static string NameOf(AiOptions ai) =>
        string.IsNullOrWhiteSpace(ai.BaseUrl) || ai.Info.Owns(ai.BaseUrl) ? ai.Known : $"The AI provider at {ai.ResolvedBaseUrl}";

    /// <summary>The SDK refuses an empty key; a server that takes none ignores whatever it's sent.</summary>
    static string KeyOf(AiOptions ai) => string.IsNullOrWhiteSpace(ai.ApiKey) ? "none" : ai.ApiKey.Trim();

    // Fixed wording rather than the SDK's exception message: that can carry whatever the far end
    // answered, and the far end is an address an administrator typed in.
    internal static SWValidationException Unreachable(string provider, string detail) =>
        new("AI_UNREACHABLE", $"{provider} can't be reached right now: {detail}");

    internal static SWValidationException Refused(string provider, string detail) =>
        new("AI_PROVIDER_ERROR", $"{provider} refused the request: {detail}");

    internal const string NoConnection =
        "the connection failed or timed out, or the address isn't one Bitween may connect to.";

    internal static string Answered(int status) => $"it answered HTTP {status}.";
}
