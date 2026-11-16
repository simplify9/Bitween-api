using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Services.Ai;

/// <summary>
/// A provider Settings, AI offers, and where its API is. Exactly one of <see cref="Address"/>,
/// <see cref="Template"/> or neither: an own address, one built from a part only the customer knows
/// (an Azure resource, an AWS region), or - for <see cref="AiOptions.Other"/> - one typed in whole.
/// </summary>
/// <param name="Address">Its own address, used while the API address setting is empty.</param>
/// <param name="Places">Other addresses it serves from, such as regions, by name.</param>
/// <param name="Template">The address with <c>{0}</c> where <see cref="Part"/> goes.</param>
/// <param name="Part">What the customer fills in, e.g. "AWS region".</param>
/// <param name="Example">An example of <see cref="Part"/>, or of a whole address for Other.</param>
/// <param name="Keyless">Takes no API key.</param>
/// <param name="Lists">Lists its models; false where only the provider's console shows the names.</param>
/// <param name="ModelHint">What to type as the model where it doesn't list them.</param>
public record AiProvider(
    string Name,
    string Address = null,
    AiPlace[] Places = null,
    string Template = null,
    string Part = null,
    string Example = null,
    bool Keyless = false,
    bool Lists = true,
    string ModelHint = null)
{
    /// <summary>Whether an address is one of this provider's own - its address, a place or its template filled in.</summary>
    public bool Owns(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var trimmed = address.Trim();
        if (string.Equals(trimmed, Address, StringComparison.OrdinalIgnoreCase)) return true;
        if (Places?.Any(p => string.Equals(trimmed, p.Address, StringComparison.OrdinalIgnoreCase)) == true) return true;
        if (Template is null) return false;
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(Template).Replace(@"\{0}", "[A-Za-z0-9-]+") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

/// <summary>One of a provider's addresses, by the name its console gives it.</summary>
public record AiPlace(string Label, string Address);

/// <summary>
/// The AI provider every AI feature calls - generating mapping rules first, more to follow. One
/// instance for the life of the app: Settings changes its properties in place, and each call reads
/// them, so a new key or model applies to the next request.
/// </summary>
public class AiOptions
{
    public const string Anthropic = "Anthropic";
    public const string OpenAI = "OpenAI";
    public const string AzureOpenAI = "Azure OpenAI";
    public const string Bedrock = "Amazon Bedrock";
    public const string Ollama = "Ollama";

    /// <summary>Any other server that speaks OpenAI's API - vLLM, LM Studio, a company gateway - at an address typed in.</summary>
    public const string Other = "Other (OpenAI-compatible)";

    /// <summary>
    /// In the order Settings lists them. No default models: their names change too often to keep one
    /// here, so the provider's own list is offered instead.
    /// </summary>
    public static readonly AiProvider[] Catalog =
    [
        new(Anthropic, "https://api.anthropic.com"),
        new(OpenAI, "https://api.openai.com/v1/"),
        new(AzureOpenAI, Template: "https://{0}.openai.azure.com/openai/v1/", Part: "Resource name", Example: "acme-ai",
            Lists: false, ModelHint: "Type the deployment name."),
        // bedrock-runtime, which AWS recommends; it takes a Bedrock API key but doesn't list models.
        new(Bedrock, Template: "https://bedrock-runtime.{0}.amazonaws.com/openai/v1/", Part: "AWS region", Example: "us-east-1",
            Lists: false, ModelHint: "Type the model ID from the Bedrock console."),
        new("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/"),
        new("Mistral", "https://api.mistral.ai/v1/"),
        new("Cohere", "https://api.cohere.ai/compatibility/v1/"),
        new("xAI", "https://api.x.ai/v1/"),
        new("DeepSeek", "https://api.deepseek.com/v1/"),
        new("Alibaba Cloud (Qwen)", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/",
            Places:
            [
                new("Singapore", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"),
                new("US (Virginia)", "https://dashscope-us.aliyuncs.com/compatible-mode/v1/"),
                new("China (Beijing)", "https://dashscope.aliyuncs.com/compatible-mode/v1/"),
            ]),
        new("Groq", "https://api.groq.com/openai/v1/"),
        new("Together AI", "https://api.together.xyz/v1/"),
        new("Fireworks AI", "https://api.fireworks.ai/inference/v1/"),
        new("Cerebras", "https://api.cerebras.ai/v1/"),
        new("OpenRouter", "https://openrouter.ai/api/v1/"),
        new(Ollama, "http://localhost:11434/v1/", Keyless: true),
        new(Other, Example: "https://llm.example.com/v1/", Keyless: true),
    ];

    public static readonly string[] Providers = Catalog.Select(p => p.Name).ToArray();

    /// <summary>What an Anthropic provider uses while <see cref="Model"/> is empty.</summary>
    public const string DefaultAnthropicModel = "claude-opus-5-5";

    public string Provider { get; set; } = Anthropic;

    public string ApiKey { get; set; }

    /// <summary>Empty uses <see cref="DefaultAnthropicModel"/> with Anthropic. Other providers need one.</summary>
    public string Model { get; set; }

    /// <summary>
    /// Where the provider's API is, when it isn't the provider's own - an Azure resource, an AWS
    /// region, a company proxy. Empty uses the provider's own address.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>The provider as <see cref="Catalog"/> describes it, or null when it isn't one of them.</summary>
    public AiProvider Info => Catalog.FirstOrDefault(p => string.Equals(p.Name, Provider?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The provider as <see cref="Providers"/> spells it, or null when it isn't one of them.</summary>
    public string Known => Info?.Name;

    public bool IsAnthropic => Known == Anthropic;

    public bool NeedsKey => Info is not { Keyless: true };

    /// <summary><see cref="BaseUrl"/>, or the provider's own address while that's empty; null when it has none to give.</summary>
    public string ResolvedBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl.Trim() : Info?.Address;

    public string ResolvedModel =>
        !string.IsNullOrWhiteSpace(Model) ? Model.Trim() : IsAnthropic ? DefaultAnthropicModel : null;

    /// <summary>
    /// Whether AI features can run: a provider Bitween knows, a key unless it takes none, a model to
    /// send it with, and an address. A misspelt provider in configuration is off, not quietly sent
    /// down the OpenAI path.
    /// </summary>
    public bool IsSet => Known is not null && (!NeedsKey || !string.IsNullOrWhiteSpace(ApiKey)) && ResolvedModel is not null
                         && ResolvedBaseUrl is not null;

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
