using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Writes <c>__partner__</c> and <c>__globals__</c> into a JSON payload for a mapper that reads them
/// out of it (see <c>XchangeService.RunMapper</c>).
/// </summary>
/// <remarks>
/// Its own class so the golden payload tests run the very code the pipeline runs.
/// </remarks>
public static class MapperEnrichment
{
    /// <summary>
    /// The payload as the enrichment reads it: <c>JToken.Parse</c>, which throws on what is not JSON,
    /// and null for JSON that is not an object (a root array, a string), which is not enriched.
    /// </summary>
    public static JObject Read(XchangeFile file)
    {
        NativeAdapters.PayloadParseTally.Parsed();
        return JToken.Parse(file.Data) as JObject;
    }

    /// <summary>
    /// The payload with the partner's adapter properties and every global values set written in, as
    /// text; null when there is nothing to write, so the payload goes on exactly as it arrived.
    /// </summary>
    public static string Enrich(JObject payload, IDictionary<string, string> partnerProperties,
        IEnumerable<GlobalAdapterValuesSet> globalSets)
    {
        var enriched = false;

        if (partnerProperties?.Count > 0)
        {
            payload["__partner__"] = JObject.FromObject(partnerProperties);
            enriched = true;
        }

        var sets = globalSets?.Where(s => s.Values?.Count > 0).ToList() ?? [];
        if (sets.Count > 0)
        {
            var globalsObj = new JObject();
            foreach (var set in sets)
                globalsObj[set.Id] = JObject.FromObject(set.Values);
            payload["__globals__"] = globalsObj;
            enriched = true;
        }

        return enriched ? payload.ToString(Formatting.None) : null;
    }
}
