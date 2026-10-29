using System;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// The AI provider every AI feature calls - generating mapping rules first, more to follow. One
/// instance for the life of the app: Settings changes its properties in place, and each call reads
/// them, so a new key or model applies to the next request.
/// </summary>
public class AiOptions
{
    public const string Anthropic = "Anthropic";

    /// <summary>OpenAI, or anything that speaks its API at <see cref="BaseUrl"/>.</summary>
    public const string OpenAI = "OpenAI";

    public static readonly string[] Providers = [Anthropic, OpenAI];

    /// <summary>What an Anthropic provider uses while <see cref="Model"/> is empty.</summary>
    public const string DefaultAnthropicModel = "claude-opus-5-5";

    public string Provider { get; set; } = Anthropic;

    public string ApiKey { get; set; }

    /// <summary>Empty uses <see cref="DefaultAnthropicModel"/> with Anthropic. Other providers need one.</summary>
    public string Model { get; set; }

    /// <summary>
    /// Where the provider's API is, when it isn't the provider's own - an Azure OpenAI resource, a
    /// local Ollama, a company proxy. Empty uses the provider's public endpoint.
    /// </summary>
    public string BaseUrl { get; set; }

    public bool IsAnthropic => string.Equals(Provider?.Trim(), Anthropic, StringComparison.OrdinalIgnoreCase);

    public bool IsOpenAI => string.Equals(Provider?.Trim(), OpenAI, StringComparison.OrdinalIgnoreCase);

    public string ResolvedModel =>
        !string.IsNullOrWhiteSpace(Model) ? Model.Trim() : IsAnthropic ? DefaultAnthropicModel : null;

    /// <summary>
    /// Whether AI features can run: a provider Bitween knows, a key, and a model to send it with. A
    /// misspelt provider in configuration is off, not quietly sent down the OpenAI path.
    /// </summary>
    public bool IsSet => (IsAnthropic || IsOpenAI) && !string.IsNullOrWhiteSpace(ApiKey) && ResolvedModel is not null;

    /// <summary>
    /// Why an API address can't be used, or null when it can - or is empty. Plain http only reaches
    /// this machine, such as a local Ollama: anywhere else it would send the key in the clear.
    /// </summary>
    public static string AddressProblem(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            return $"'{address.Trim()}' isn't an http or https address.";
        if (url.Scheme == "http" && !url.IsLoopback)
            return "plain http is only for this machine (localhost); use https anywhere else, so the key isn't sent in the clear.";
        return null;
    }
}
