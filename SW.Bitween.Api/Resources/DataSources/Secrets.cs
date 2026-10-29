using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Resources.DataSources;

/// <summary>
/// Keeps a data source's credentials out of responses.
///
/// This does NOT ask the adapter which of its startup values are marked secure, the way
/// <see cref="AdapterSecretProperties"/> does for subscription properties. Describing an adapter
/// means starting it, and a bus provider is resident - started once and held for the life of the
/// node - so asking would either start a second copy of a connection that is meant to be exclusive
/// or block on one that is already running. The data source names its own secrets instead, which
/// also lets an operator protect a field the adapter author never thought to mark.
/// </summary>
public static class Secrets
{
    /// <summary>Stands in for a stored secret. Deliberately the same sentinel the rest of the app uses.</summary>
    public const string Sentinel = AdapterSecretProperties.Sentinel;

    /// <summary>
    /// Names that are treated as secret whether or not anyone listed them. A credential missed
    /// because nobody ticked a box is a credential in a JSON response, so the default is to hide.
    /// </summary>
    private static readonly string[] AlwaysSecret =
    [
        "password", "secret", "token", "credential", "apikey", "accesskey",
        "privatekey", "connectionstring", "sas", "passphrase", "certificate"
    ];

    public static bool IsSecret(string name, IEnumerable<string> declared) =>
        (declared ?? []).Contains(name, StringComparer.OrdinalIgnoreCase) ||
        AlwaysSecret.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A copy with every secret replaced. An empty value is left alone, so "not set" stays
    /// distinguishable from "set but hidden" - the difference between a form that is finished and
    /// one that is not.
    /// </summary>
    public static Dictionary<string, string> Mask(
        IReadOnlyDictionary<string, string> properties, IEnumerable<string> declared)
    {
        if (properties == null) return new Dictionary<string, string>();

        var secretNames = declared?.ToList() ?? [];
        return properties.ToDictionary(kv => kv.Key,
            kv => IsSecret(kv.Key, secretNames) && !string.IsNullOrEmpty(kv.Value)
                ? Sentinel
                : kv.Value);
    }

    /// <summary>
    /// Resolves sentinels against what is stored. A sentinel with nothing behind it is dropped
    /// rather than saved literally - otherwise a data source copied from a response would
    /// authenticate with the string "__private__".
    /// </summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, string> incoming)
    {
        var result = new Dictionary<string, string>();
        if (incoming == null) return result;

        foreach (var kv in incoming)
        {
            if (kv.Value != Sentinel) result[kv.Key] = kv.Value;
            else if (stored != null && stored.TryGetValue(kv.Key, out var storedValue))
                result[kv.Key] = storedValue;
        }

        return result;
    }

    /// <summary>
    /// The secret names to record. Whatever the caller declared, plus anything matching a
    /// well-known credential name - so the list stays true even when the form did not tick it.
    /// </summary>
    public static List<string> Declare(
        IReadOnlyDictionary<string, string> properties, IEnumerable<string> declared)
    {
        var names = new HashSet<string>(declared ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var key in properties?.Keys ?? Enumerable.Empty<string>())
            if (AlwaysSecret.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
                names.Add(key);

        return names.ToList();
    }
}
