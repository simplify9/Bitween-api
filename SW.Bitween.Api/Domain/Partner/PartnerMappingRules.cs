using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// What a partner may save as their own mapping (<see cref="PartnerMapping"/>): native mapper rules
/// that read the document, their own partner values, and no global value the subscription's own
/// mapping doesn't already send them.
/// </summary>
/// <remarks>
/// A real delivery fills in the real values, so a rule reading a global value would send it to the
/// partner's own system. Those the subscription's mapping reads already go to them, which is why
/// starting from it works; any other is refused. The same goes for a <c>{{globals.…}}</c> written
/// into the settings, which is filled in before the mapper runs.
/// </remarks>
public static class PartnerMappingRules
{
    /// <summary>The adapter id of the native mapper, the only one a partner's mapping runs on.</summary>
    public const string MapperId = nameof(NativeMapper);

    public static readonly string[] Keys =
        [nameof(NativeMapperInput.MappingRules), nameof(NativeMapperInput.SourceSample), nameof(NativeMapperInput.TargetSample)];

    /// <summary>Only the native mapper's settings, the rules readable and within reach.</summary>
    public static Dictionary<string, string> Validate(IDictionary<string, string> properties,
        IReadOnlyDictionary<string, string> subscriptionMapperProperties)
    {
        var kept = Keys.ToDictionary(k => k, k => properties != null && properties.TryGetValue(k, out var v) ? v ?? "" : "");
        if (string.IsNullOrWhiteSpace(kept[nameof(NativeMapperInput.MappingRules)]))
            throw new SWValidationException("NO_RULES", "The mapping has no rules.");

        var allowed = GlobalsIn(subscriptionMapperProperties);
        var used = GlobalsIn(kept);
        var beyond = used.Except(allowed).OrderBy(g => g).ToList();
        if (beyond.Count > 0)
            throw new SWValidationException("GLOBAL_VALUES_NOT_ALLOWED",
                "Your mapping can't read these internal values: " + string.Join(", ", beyond) + ".");
        return kept;
    }

    /// <summary>Every global value the settings read, as <c>set.key</c>: by rule, and by placeholder.</summary>
    public static HashSet<string> GlobalsIn(IReadOnlyDictionary<string, string> properties)
    {
        var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (properties is null) return found;

        foreach (var reference in StartupValuesFiller.ReferencesIn(properties.Values).Globals)
            foreach (var key in reference.Keys)
                found.Add($"{reference.SetId}.{key}");

        if (properties.TryGetValue(nameof(NativeMapperInput.MappingRules), out var json) && !string.IsNullOrWhiteSpace(json))
        {
            MappingRules rules;
            try
            {
                rules = JsonConvert.DeserializeObject<MappingRules>(json) ?? new MappingRules();
            }
            catch (JsonException)
            {
                throw new SWValidationException("UNREADABLE_RULES", "The mapping rules couldn't be read.");
            }

            foreach (var field in AllFields(rules))
                if (field.From?.Kind == ValueSourceKind.Global)
                    found.Add($"{field.From.SetId}.{field.From.Key}");
        }
        return found;
    }

    private static IEnumerable<FieldRule> AllFields(MappingRules rules) =>
        rules.Fields.Concat(rules.Lists.Concat(rules.Root is null ? [] : [rules.Root]).SelectMany(AllFields));

    private static IEnumerable<FieldRule> AllFields(ListRule list) =>
        (list.Item is null ? [] : new[] { list.Item })
        .Concat(list.Fields)
        .Concat(list.Lists.SelectMany(AllFields))
        .Concat(list.Fixed.Concat(list.After).SelectMany(AllFields));

    private static IEnumerable<FieldRule> AllFields(ListEntry entry) =>
        (entry.Item is null ? [] : new[] { entry.Item })
        .Concat(entry.Fields)
        .Concat(entry.Lists.SelectMany(AllFields));
}
