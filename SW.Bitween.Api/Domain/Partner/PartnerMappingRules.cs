using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters.Mapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// What a partner may save as their own mapping (<see cref="PartnerMapping"/>): native mapper rules
/// that read the document, the partner properties they may see, and no global value or other partner
/// property the subscription's own mapping doesn't already send them.
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

    /// <summary>The one setting a partner's mapping keeps: the rules.</summary>
    public const string RulesKey = nameof(NativeMapperInput.MappingRules);

    /// <summary>
    /// Only the rules, readable and within reach. The editor's samples are dropped: one can be a real
    /// document from the partner's traffic, and nothing that runs reads them.
    /// </summary>
    public static Dictionary<string, string> Validate(IDictionary<string, string> properties,
        IReadOnlyDictionary<string, string> subscriptionMapperProperties, IReadOnlySet<string> visiblePartnerKeys)
    {
        var rules = properties != null && properties.TryGetValue(RulesKey, out var r) ? r ?? "" : "";
        if (string.IsNullOrWhiteSpace(rules))
            throw new SWValidationException("NO_RULES", "The mapping has no rules.");

        var beyond = GlobalsBeyond(rules, subscriptionMapperProperties).OrderBy(g => g).ToList();
        if (beyond.Count > 0)
            throw new SWValidationException("GLOBAL_VALUES_NOT_ALLOWED",
                "Your mapping can't read these internal values: " + string.Join(", ", beyond) + ".");

        var hidden = PartnerKeysBeyond(rules, subscriptionMapperProperties, visiblePartnerKeys).OrderBy(k => k).ToList();
        if (hidden.Count > 0)
            throw new SWValidationException("PARTNER_VALUES_NOT_ALLOWED",
                "Your mapping can't read these of your details: " + string.Join(", ", hidden) + ".");
        return new Dictionary<string, string> { [RulesKey] = rules };
    }

    /// <summary>
    /// Whether a stored partner mapping still reads only what the subscription's mapping sends. One
    /// that doesn't, because ours stopped reading a value since it was saved, is set aside: the
    /// partner gets ours until they change theirs.
    /// </summary>
    public static bool Fits(PartnerMapping mapping, IReadOnlyDictionary<string, string> subscriptionMapperProperties,
        IReadOnlySet<string> visiblePartnerKeys)
    {
        try
        {
            var rules = RulesOf(mapping.MapperProperties);
            return !GlobalsBeyond(rules, subscriptionMapperProperties).Any() &&
                   !PartnerKeysBeyond(rules, subscriptionMapperProperties, visiblePartnerKeys).Any();
        }
        catch (SWValidationException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GlobalsBeyond(string rules, IReadOnlyDictionary<string, string> subscriptionMapperProperties) =>
        GlobalsIn(rules).Except(GlobalsIn(RulesOf(subscriptionMapperProperties)), System.StringComparer.OrdinalIgnoreCase);

    /// <summary>The partner properties the rules read that the partner can't see and ours doesn't read.</summary>
    private static IEnumerable<string> PartnerKeysBeyond(string rules,
        IReadOnlyDictionary<string, string> subscriptionMapperProperties, IReadOnlySet<string> visiblePartnerKeys) =>
        PartnerKeysIn(rules)
            .Where(k => visiblePartnerKeys is null || !visiblePartnerKeys.Contains(k))
            .Except(PartnerKeysIn(RulesOf(subscriptionMapperProperties)), System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Every partner property the rules read: by rule, and by <c>{{partner.…}}</c> placeholder.</summary>
    public static HashSet<string> PartnerKeysIn(string rulesJson)
    {
        var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rulesJson)) return found;
        foreach (var key in StartupValuesFiller.ReferencesIn([rulesJson]).PartnerProperties)
            found.Add(key);
        foreach (var field in AllFields(Parse(rulesJson)))
            if (field.From?.Kind == ValueSourceKind.Partner && !string.IsNullOrEmpty(field.From.Key))
                found.Add(field.From.Key);
        return found;
    }

    private static MappingRules Parse(string rulesJson)
    {
        try
        {
            return JsonConvert.DeserializeObject<MappingRules>(rulesJson) ?? new MappingRules();
        }
        catch (JsonException)
        {
            throw new SWValidationException("UNREADABLE_RULES", "The mapping rules couldn't be read.");
        }
    }

    private static string RulesOf(IReadOnlyDictionary<string, string> properties) =>
        properties != null && properties.TryGetValue(RulesKey, out var rules) ? rules ?? "" : "";

    /// <summary>Every global value the rules read, as <c>set.key</c>: by rule, and by placeholder.</summary>
    public static HashSet<string> GlobalsIn(string rulesJson)
    {
        var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rulesJson)) return found;

        // A placeholder is filled in before the mapper runs, wherever it is written in the rules.
        foreach (var reference in StartupValuesFiller.ReferencesIn([rulesJson]).Globals)
            foreach (var key in reference.Keys)
                found.Add($"{reference.SetId}.{key}");

        foreach (var field in AllFields(Parse(rulesJson)))
            if (field.From?.Kind == ValueSourceKind.Global)
                found.Add($"{field.From.SetId}.{field.From.Key}");
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
