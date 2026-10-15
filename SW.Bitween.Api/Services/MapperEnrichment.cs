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
    /// <remarks>
    /// <paramref name="shared"/> says the object is the pipeline's shared parse, which
    /// <see cref="Enrich"/> must copy before it writes.
    /// </remarks>
    public static JObject Read(XchangeFile file, out bool shared)
    {
        if (NativeAdapters.ParsedPayload.Of(file) is { } payload &&
            payload.TryGet(NativeAdapters.ParseProfiles.Defaults, file.Data, out var tree))
        {
            shared = true;
            return tree as JObject;
        }

        shared = false;
        NativeAdapters.PayloadParseTally.Parsed();
        return JToken.Parse(file.Data) as JObject;
    }

    /// <summary>
    /// The payload with the partner's adapter properties and every global values set written in, as
    /// text; null when there is nothing to write, so the payload goes on exactly as it arrived.
    /// </summary>
    /// <remarks>
    /// A <paramref name="shared"/> payload is the tree every other step of the exchange reads, so it is
    /// copied before anything is written: the copy is written to and serialized, the shared tree is
    /// left as it was parsed. Not copied when there is nothing to write.
    /// </remarks>
    public static string Enrich(JObject payload, bool shared, IDictionary<string, string> partnerProperties,
        IEnumerable<GlobalAdapterValuesSet> globalSets)
    {
        var writePartner = partnerProperties?.Count > 0;
        var sets = globalSets?.Where(s => s.Values?.Count > 0).ToList() ?? [];
        if (!writePartner && sets.Count == 0) return null;

        if (shared) payload = (JObject)payload.DeepClone();

        if (writePartner)
            payload["__partner__"] = JObject.FromObject(partnerProperties);

        if (sets.Count > 0)
        {
            var globalsObj = new JObject();
            foreach (var set in sets)
                globalsObj[set.Id] = JObject.FromObject(set.Values);
            payload["__globals__"] = globalsObj;
        }

        return payload.ToString(Formatting.None);
    }
}
