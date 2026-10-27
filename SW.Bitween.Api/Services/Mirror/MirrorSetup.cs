using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// A value of prod's the copy never brings, for staging's own: an address or a secret, which arrives
/// as a placeholder (see <see cref="MirrorFaker.Placeholder"/>). Staging's value is kept wherever staging
/// has one, so only a placeholder is left to do.
/// </summary>
/// <param name="Path">Where it is in the item, as its configuration file says it: handler, properties, Url.</param>
/// <param name="Field">Where it is, in words: handler · Url.</param>
/// <param name="Kind">address, secret or login: what staging's own value is.</param>
/// <param name="StoodIn">
/// A secret a bring saved as a placeholder, which Bitween hides like any saved secret: open until a person
/// says it was filled in.
/// </param>
public record MirrorBlank(string Section, string Key, IReadOnlyList<string> Path, string Field, string Kind, bool StoodIn = false);

/// <summary>A partner property or global value that is faked, unless it is kept because it is a setting.</summary>
public record MirrorFakedKey(string Id, string Label, int Count, bool Kept);

/// <summary>Prod's setup as staging gets it: every value handled, and what that turned up.</summary>
/// <param name="Blanks">Every address and secret of prod's the copy left empty.</param>
/// <param name="SwitchedOff">
/// The data sources and bus gateways prod has on and the copy switched off, as section/key: a test's
/// traffic still goes through them, as prod's does.
/// </param>
public record MirrorSetupCopy(
    Dictionary<string, List<JsonObject>> Sections,
    IReadOnlyList<MirrorBlank> Blanks,
    IReadOnlyList<MirrorFakedKey> FakedKeys,
    IReadOnlySet<string> SwitchedOff = null);

/// <summary>
/// Prod's setup, read as a configuration file would say it, with what staging must not hold taken out:
/// <list type="bullet">
/// <item>how flows behave is copied as it is - formats, schedules, filters, mapping rules, tokens;</item>
/// <item>addresses and secrets never come: placeholders stand in until staging's own are filled in - the team's test systems and logins;</item>
/// <item>partners' API keys become keys of staging's own;</item>
/// <item>partner properties and global values, which are someone's data, are faked to the same shape,
/// unless a key is kept as a setting.</item>
/// </list>
/// Subscriptions arrive paused, and data sources and bus gateways switched off, so nothing on staging
/// runs on its own until a test runs it.
/// </summary>
public static class MirrorSetup
{
    public static MirrorSetupCopy Copy(IReadOnlyDictionary<string, List<Existing>> prod, MirrorFaker faker, ISet<string> keep)
    {
        var sections = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var blanks = new List<MirrorBlank>();
        foreach (var (section, items) in prod)
            sections[section] = items.Where(e => e.Key != null).OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => Handle(section, e.Key, (JsonObject)e.File.DeepClone(), faker, keep, blanks)).ToList();
        var switchedOff = prod.Where(p => p.Key is "dataSources" or "busGateways")
            .SelectMany(p => p.Value.Where(e => e.Key != null && !Values.Same(e.File["inactive"], true)).Select(e => $"{p.Key}/{e.Key}"))
            .ToHashSet(StringComparer.Ordinal);

        return new MirrorSetupCopy(sections, blanks, FakedKeys(prod, keep), switchedOff);
    }

    static JsonObject Handle(string section, string key, JsonObject item, MirrorFaker faker, ISet<string> keep, List<MirrorBlank> blanks)
    {
        var where = $"{section}/{key}";
        void Blank(JsonObject owner, string name, string kind, params string[] path)
        {
            owner[name] = MirrorFaker.Placeholder(kind, name, (string)owner[name] ?? "");
            blanks.Add(new MirrorBlank(section, key, path, string.Join(" · ", path.Where(p => p != "properties")), kind));
        }

        switch (section)
        {
            case "partners":
                Data(item, "properties", (k, _) => keep.Contains(PartnerKey(k)), faker, Blank);
                if ((string)item["loginIdentity"] is { Length: > 0 } identity)
                    item["loginIdentity"] = faker.Datum("loginIdentity", identity);
                // Prod keeps only a hash of each; staging gets keys of its own, the same each copy.
                if (item["keys"] is JsonObject keys)
                    foreach (var name in keys.Select(k => k.Key).ToList())
                        keys[name] = faker.Secret($"{where}/key/{name}");
                break;

            case "globalValues":
                Data(item, "values", (k, _) => keep.Contains(GlobalKey(key, k)), faker, Blank);
                break;

            case "connections":
                // Where it logs in, and as whom: the team's test login, never prod's.
                foreach (var field in new[] { "authorizeUrl", "tokenUrl" })
                    if ((string)item[field] is { Length: > 0 }) Blank(item, field, "address", field);
                foreach (var field in new[] { "clientId", "username", "clientSecret", "password" })
                    if ((string)item[field] is { Length: > 0 }) Blank(item, field, "login", field);
                break;

            case "notificationChannels":
            case "notifiers":
            case "dataSources":
                Settings(item, Blank, "properties");
                if (section == "dataSources") item["inactive"] = true;
                break;

            case "busGateways":
                Settings(item, Blank, "endpointProperties");
                item["inactive"] = true;
                break;

            case "apiGateways":
                // Its issuer is where partner logins are checked: an address like any other.
                Settings(item, Blank, "authentication");
                break;

            case "subscriptions":
                foreach (var role in new[] { "receiver", "validator", "mapper", "handler" })
                    Settings(item[role] as JsonObject, (owner, name, kind, path) => Blank(owner, name, kind, [role, .. path]), "properties");
                item["paused"] = true;
                break;
        }
        return item;
    }

    delegate void Blanker(JsonObject owner, string name, string kind, params string[] path);

    /// <summary>How an adapter behaves: copied, except addresses and secrets, which are left empty.</summary>
    static void Settings(JsonObject item, Blanker blank, string field)
    {
        if (item?[field] is not JsonObject properties) return;
        foreach (var name in properties.Select(p => p.Key).ToList())
            if (properties[name] is JsonValue v && v.TryGetValue<string>(out var value) && MirrorFaker.BlankKind(name, value) is { } kind)
                blank(properties, name, kind, field, name);
    }

    /// <summary>Someone's data: faked, unless kept; an address or a secret in it is left empty, as anywhere.</summary>
    static void Data(JsonObject item, string field, Func<string, string, bool> kept, MirrorFaker faker, Blanker blank)
    {
        if (item[field] is not JsonObject values) return;
        foreach (var name in values.Select(p => p.Key).ToList())
            if (values[name] is JsonValue v && v.TryGetValue<string>(out var value))
            {
                if (MirrorFaker.BlankKind(name, value) is { } kind) blank(values, name, kind, field, name);
                else values[name] = faker.Datum(name, value, kept(name, value));
            }
    }

    // ------------------------------------------------------------------ the words it names

    /// <summary>
    /// Every word staging's copy of prod's setup names, outside the data in it - partners' properties,
    /// global values, logins: a filter's 'SHIPPED', a file pattern's ORD, a global value's key, names.
    /// Read from the copy, after its addresses and data are faked, so a word of it is one staging holds
    /// already. Faking keeps these as they are (see <see cref="MirrorFaker"/>), so a value of prod's that
    /// is one of them takes the path the real one took.
    /// </summary>
    public static HashSet<string> Words(Dictionary<string, List<JsonObject>> sections)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, items) in sections)
        foreach (var item in items)
            Walk(item, [], (path, text) =>
            {
                if (!IsData(section, path)) AddWords(words, text);
            });
        // A global value's key is what a payload looks it up by.
        foreach (var set in sections.GetValueOrDefault("globalValues") ?? [])
        foreach (var (name, _) in set["values"] as JsonObject ?? [])
            AddWords(words, name);
        return words;
    }

    static bool IsData(string section, IReadOnlyList<string> path) => path.Count > 0 && section switch
    {
        "partners" => path[0] is "properties" or "loginIdentity" or "keys",
        "globalValues" => path[0] == "values",
        _ => false,
    };

    static void AddWords(HashSet<string> words, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is > 0 and <= MaxWord) words.Add(trimmed);
        foreach (Match m in QuotedPattern.Matches(text)) words.Add(m.Groups["q"].Value);
        foreach (Match m in WordInPattern.Matches(text)) words.Add(m.Value);
    }

    const int MaxWord = 64;
    static readonly Regex QuotedPattern = new(@"'(?<q>[^'\r\n]{1,64})'|""(?<q>[^""\r\n]{1,64})""", RegexOptions.Compiled);
    static readonly Regex WordInPattern = new(@"[\p{L}\p{M}\p{Nd}]{2,64}", RegexOptions.Compiled);

    public static string PartnerKey(string property) => $"partner:{property}";

    public static string GlobalKey(string set, string key) => $"global:{set}/{key}";

    /// <summary>
    /// Every partner property and global value that gets faked, by key, and whether it is kept. A
    /// secret or an address is never offered: neither can be kept, whatever the list says.
    /// </summary>
    static List<MirrorFakedKey> FakedKeys(IReadOnlyDictionary<string, List<Existing>> prod, ISet<string> keep)
    {
        var rows = new List<MirrorFakedKey>();
        var partnerKeys = (prod.GetValueOrDefault("partners") ?? [])
            .SelectMany(p => Offered(p.File["properties"] as JsonObject, p.File["secretProperties"]))
            .GroupBy(k => k, StringComparer.Ordinal);
        foreach (var group in partnerKeys.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            rows.Add(new MirrorFakedKey(PartnerKey(group.Key), $"Partner property {group.Key}", group.Count(), keep.Contains(PartnerKey(group.Key))));

        foreach (var set in (prod.GetValueOrDefault("globalValues") ?? []).OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        foreach (var name in Offered(set.File["values"] as JsonObject, set.File["secretProperties"]).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            rows.Add(new MirrorFakedKey(GlobalKey(set.Key, name), $"Global value {set.Key}.{name}", 1, keep.Contains(GlobalKey(set.Key, name))));
        return rows;
    }

    static IEnumerable<string> Offered(JsonObject values, JsonNode secretNames)
    {
        var secret = (secretNames as JsonArray ?? []).Select(x => x?.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values ?? [])
        {
            var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (string.IsNullOrEmpty(text) || text == MirrorFaker.Masked || secret.Contains(name)) continue;
            if (MirrorFaker.IsAddressKey(name) || MirrorFaker.IsSecretLike(name) || name.EndsWith("port", StringComparison.OrdinalIgnoreCase)) continue;
            yield return name;
        }
    }

    static void Walk(JsonNode node, List<string> path, Action<IReadOnlyList<string>, string> text)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj) Walk(value, [.. path, name], text);
                break;
            case JsonArray list:
                foreach (var value in list) Walk(value, path, text);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                text(path, s);
                break;
        }
    }
}
