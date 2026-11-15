#nullable enable
using System;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.AiChecks;

/// <summary>The AI settings as Settings shows them, saved or not.</summary>
public class AiCandidate
{
    public string? Provider { get; set; }

    /// <summary>A key typed in Settings; null uses the saved one.</summary>
    public string? ApiKey { get; set; }

    public string? Model { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>
    /// These settings as options, with the saved key filled in when none was typed. The saved key
    /// only goes where it was saved for - the same provider at the same address - so checking can't
    /// carry it anywhere saving it wouldn't.
    /// </summary>
    public AiOptions With(AiOptions saved)
    {
        var candidate = new AiOptions { Provider = Provider, ApiKey = ApiKey, Model = Model, BaseUrl = BaseUrl };
        if (!string.IsNullOrWhiteSpace(ApiKey) || !candidate.NeedsKey) return candidate;

        if (candidate.Known != saved.Known || candidate.ResolvedBaseUrl != saved.ResolvedBaseUrl)
            throw new SWValidationException("AI_KEY_NEEDED",
                $"Type the API key for {candidate.Known ?? Provider}: the saved key is only sent to the provider and address it was saved for.");

        candidate.ApiKey = saved.ApiKey;
        return candidate;
    }
}
