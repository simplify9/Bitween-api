using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Keeps adapter secrets — an api key, a mail password — out of responses, and puts them back when
/// an unchanged one is saved again.
/// </summary>
/// <remarks>
/// <para>
/// An adapter marks a startup value <c>[Secure]</c>, which is what <see cref="StartupValue.Private"/>
/// reports. <see cref="Mask"/> replaces those values with <see cref="Sentinel"/> on the way out, and
/// <see cref="Merge"/> reads the sentinel on the way back in as "keep what is stored" — so a form
/// that only changed the subject line does not overwrite the password with a row of dots.
/// </para>
/// <para>
/// The same sentinel and the same pair of steps already guard subscription adapter properties inside
/// <c>Subscriptions/Get</c> and <c>Subscriptions/Update</c>; this is the reusable form of it.
/// </para>
/// </remarks>
public class AdapterSecretProperties(AdapterStartupValues startupValues)
{
    /// <summary>Stands in for a secret value in any response that carries adapter properties.</summary>
    public const string Sentinel = "__private__";

    /// <summary>
    /// Returns a copy with every secret value replaced. Values that are already empty are left
    /// alone, so "not set" stays distinguishable from "set but hidden".
    /// </summary>
    public async Task<Dictionary<string, string>> Mask(
        string adapterId, IReadOnlyDictionary<string, string> properties)
    {
        if (properties == null || properties.Count == 0)
            return properties?.ToDictionary(kv => kv.Key, kv => kv.Value);

        // No adapter to ask about: mask nothing rather than guess. There is also nothing to send
        // the properties to, so they cannot be credentials in use.
        if (string.IsNullOrEmpty(adapterId))
            return properties.ToDictionary(kv => kv.Key, kv => kv.Value);

        IDictionary<string, StartupValue> described;
        try
        {
            described = await startupValues.Describe(adapterId);
        }
        catch
        {
            // Fail closed: when the adapter cannot be described there is no way to tell which value
            // is a secret, and guessing wrong one way leaks it.
            return properties.ToDictionary(kv => kv.Key, _ => Sentinel);
        }

        return properties.ToDictionary(kv => kv.Key, kv =>
            described.TryGetValue(kv.Key, out var startupValue)
            && startupValue.Private
            && !string.IsNullOrEmpty(kv.Value)
                ? Sentinel
                : kv.Value);
    }

    /// <summary>
    /// Masks the values whose names are in <paramref name="declared"/>, for the property bags whose
    /// secrets nobody can ask an adapter about.
    /// </summary>
    /// <remarks>
    /// A partner property or a global value is not a startup value of any one adapter — it is
    /// referenced as <c>{{partner.KEY}}</c> by however many adapters point at it, so there is no
    /// single adapter to describe. The owner names its own secrets instead, exactly as a data
    /// source does. Unlike <c>DataSources.Secrets</c> this does not also guess from the key's name:
    /// a partner property has always been readable, and inferring would hide values that an
    /// operator could see yesterday.
    /// </remarks>
    public static Dictionary<string, string> Mask(
        IReadOnlyDictionary<string, string> properties, IEnumerable<string> declared)
    {
        if (properties == null) return null;

        var secretNames = new HashSet<string>(declared ?? [], StringComparer.OrdinalIgnoreCase);
        return properties.ToDictionary(kv => kv.Key,
            kv => secretNames.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value)
                ? Sentinel
                : kv.Value);
    }

    /// <summary>
    /// Resolves the sentinels in <paramref name="incoming"/> against what is already stored. A
    /// sentinel with nothing stored under that key is dropped rather than saved literally.
    /// </summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, string> incoming)
    {
        if (incoming == null) return null;

        var result = new Dictionary<string, string>();
        foreach (var kv in incoming)
        {
            if (kv.Value != Sentinel)
            {
                result[kv.Key] = kv.Value;
            }
            else if (stored != null && stored.TryGetValue(kv.Key, out var storedValue))
            {
                result[kv.Key] = storedValue;
            }
        }
        return result;
    }

    /// <summary>
    /// Whether a sentinel may still mean "keep what is stored". Not once the adapter is a different
    /// one, or a property saying where it connects has changed: a secret kept across that goes to
    /// wherever the editor pointed it — their own server, say — without their ever having seen it.
    /// Re-entering it then proves they know it.
    /// </summary>
    public static bool MayKeepStoredSecrets(string storedAdapterId, string newAdapterId,
        IReadOnlyDictionary<string, string> stored, IEnumerable<KeyValuePair<string, string>> incoming)
    {
        if (!string.Equals(storedAdapterId ?? "", newAdapterId ?? "", StringComparison.OrdinalIgnoreCase))
            return false;

        var before = stored ?? new Dictionary<string, string>();
        var after = (incoming ?? []).Where(kv => kv.Key != null)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        foreach (var key in before.Keys.Concat(after.Keys).Where(IsDestination).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(key, out var was);
            after.TryGetValue(key, out var now);
            if (now == Sentinel) continue;
            if (!string.Equals(was ?? "", now ?? "", StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static readonly string[] DestinationWords =
        ["url", "uri", "host", "server", "endpoint", "address", "domain", "port", "connectionstring"];

    /// <summary>A property that says where an adapter connects, judged by its name.</summary>
    public static bool IsDestination(string key) =>
        key != null && DestinationWords.Any(w => key.Replace("_", "").Replace("-", "")
            .Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <see cref="Merge(IReadOnlyDictionary{string,string},IReadOnlyDictionary{string,string})"/>,
    /// except that when <paramref name="keepStored"/> is false a sentinel restores nothing — the
    /// secret has to be entered again.
    /// </summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> stored,
        IReadOnlyDictionary<string, string> incoming, bool keepStored) =>
        Merge(keepStored ? stored : null, incoming);

    /// <summary>
    /// <see cref="Mask"/> applied to the dictionary the caller already holds.
    /// </summary>
    /// <remarks>
    /// <see cref="RetryGroup"/> is an immutable value object — every property is <c>init</c> — so a
    /// group's properties cannot be swapped for a masked copy. Editing the dictionary in place is
    /// the way to reach them without either loosening that contract or rebuilding each group
    /// property by property, which would silently drop whatever property is added to it next.
    /// </remarks>
    public async Task MaskInPlace(string adapterId, Dictionary<string, string> properties)
    {
        if (properties == null || properties.Count == 0) return;

        var masked = await Mask(adapterId, properties);
        properties.Clear();
        foreach (var kv in masked) properties[kv.Key] = kv.Value;
    }

    /// <summary><see cref="Merge"/> applied to the dictionary the caller already holds.</summary>
    public static void MergeInPlace(
        IReadOnlyDictionary<string, string> stored, Dictionary<string, string> incoming)
    {
        if (incoming == null || incoming.Count == 0) return;

        var merged = Merge(stored, incoming);
        incoming.Clear();
        foreach (var kv in merged) incoming[kv.Key] = kv.Value;
    }

}
