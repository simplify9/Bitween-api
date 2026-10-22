using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.Services.Mirror;

/// <summary>An address on staging that stands in for one of prod's, and what uses it.</summary>
public record MirrorAddress(string Mock, string Kind, IReadOnlyList<MirrorUse> UsedBy);

/// <summary>An item that uses something, and where in it: subscription "Push labels", handler, Url.</summary>
public record MirrorUse(string Section, string Noun, string Key, string Where);

/// <summary>A partner property or global value that is faked, unless it is kept because it is a setting.</summary>
public record MirrorFakedKey(string Id, string Label, int Count, bool Kept);

/// <summary>Prod's setup as staging gets it: every value handled, and what that turned up.</summary>
public record MirrorSetupCopy(
    Dictionary<string, List<JsonObject>> Sections,
    IReadOnlyList<MirrorAddress> Addresses,
    IReadOnlyList<MirrorFakedKey> FakedKeys);

/// <summary>
/// Prod's setup, read as a configuration file would say it, with what staging must not hold taken out:
/// <list type="bullet">
/// <item>how flows behave is copied as it is - formats, schedules, filters, mapping rules, tokens;</item>
/// <item>addresses become mock ones that resolve nowhere;</item>
/// <item>secrets prod wouldn't show become stand-ins, and partner keys new ones;</item>
/// <item>partner properties and global values, which are someone's data, are faked to the same shape,
/// unless a key is kept as a setting.</item>
/// </list>
/// Subscriptions arrive paused, and data sources and bus gateways switched off, so nothing on staging
/// runs on its own until a rehearsal runs it.
/// </summary>
public static class MirrorSetup
{
    public static MirrorSetupCopy Copy(IReadOnlyDictionary<string, List<Existing>> prod, MirrorFaker faker, ISet<string> keep)
    {
        var sections = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (var (section, items) in prod)
            sections[section] = items.Where(e => e.Key != null).OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => Handle(section, e.Key, (JsonObject)e.File.DeepClone(), faker, keep)).ToList();

        return new MirrorSetupCopy(sections, AddressesIn(sections), FakedKeys(prod, keep));
    }

    static JsonObject Handle(string section, string key, JsonObject item, MirrorFaker faker, ISet<string> keep)
    {
        var where = $"{section}/{key}";
        switch (section)
        {
            case "partners":
                Data(item["properties"] as JsonObject, $"{where}/properties", (k, _) => keep.Contains(PartnerKey(k)), faker);
                if ((string)item["loginIdentity"] is { Length: > 0 } identity)
                    item["loginIdentity"] = faker.Datum("loginIdentity", identity, $"{where}/loginIdentity");
                // Prod keeps only a hash of each; staging gets keys of its own, the same each copy.
                if (item["keys"] is JsonObject keys)
                    foreach (var name in keys.Select(k => k.Key).ToList())
                        keys[name] = faker.Secret($"{where}/key/{name}");
                break;

            case "globalValues":
                Data(item["values"] as JsonObject, $"{where}/values", (k, _) => keep.Contains(GlobalKey(key, k)), faker);
                break;

            case "connections":
                foreach (var field in new[] { "authorizeUrl", "tokenUrl" })
                    if ((string)item[field] is { } url) item[field] = faker.Setting(field, url, $"{where}/{field}");
                foreach (var field in new[] { "clientId", "username" })
                    if ((string)item[field] is { } value) item[field] = faker.Datum(field, value, $"{where}/{field}");
                foreach (var field in new[] { "clientSecret", "password" })
                    if ((string)item[field] is { Length: > 0 } value) item[field] = faker.Setting(field, value, $"{where}/{field}");
                break;

            case "notificationChannels":
            case "notifiers":
                Settings(item["properties"] as JsonObject, $"{where}/properties", faker);
                break;

            case "dataSources":
                Settings(item["properties"] as JsonObject, $"{where}/properties", faker);
                item["inactive"] = true;
                break;

            case "busGateways":
                Settings(item["endpointProperties"] as JsonObject, $"{where}/endpointProperties", faker);
                item["inactive"] = true;
                break;

            case "apiGateways":
                // Its issuer is where partner logins are checked: an address like any other.
                if (item["authentication"] is JsonObject auth)
                    foreach (var name in auth.Select(p => p.Key).ToList())
                        if ((string)auth[name] is { } value && auth[name] is JsonValue)
                            auth[name] = faker.Setting(name, value, $"{where}/authentication/{name}");
                break;

            case "subscriptions":
                foreach (var role in new[] { "receiver", "validator", "mapper", "handler" })
                    Settings(item[role]?["properties"] as JsonObject, $"{where}/{role}", faker);
                item["paused"] = true;
                break;
        }
        return item;
    }

    /// <summary>How an adapter behaves: copied, except addresses and secrets.</summary>
    static void Settings(JsonObject properties, string where, MirrorFaker faker)
    {
        if (properties == null) return;
        foreach (var name in properties.Select(p => p.Key).ToList())
            if (properties[name] is JsonValue v && v.TryGetValue<string>(out var value))
                properties[name] = faker.Setting(name, value, $"{where}/{name}");
    }

    /// <summary>Someone's data: faked, unless kept.</summary>
    static void Data(JsonObject values, string where, Func<string, string, bool> kept, MirrorFaker faker)
    {
        if (values == null) return;
        foreach (var name in values.Select(p => p.Key).ToList())
            if (values[name] is JsonValue v && v.TryGetValue<string>(out var value))
                values[name] = faker.Datum(name, value, $"{where}/{name}", kept(name, value));
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
        "connections" => path[0] is "clientId" or "username" or "clientSecret" or "password",
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

    // ------------------------------------------------------------------ the mock addresses it made

    static readonly Regex Mock = new(@"(?<before>[A-Za-z][A-Za-z0-9+.\-]*://(?:[^@/\s]*@)?|@)?(?<host>m[0-9a-f]{8}\." + Regex.Escape(MirrorFaker.MockDomain) + ")", RegexOptions.Compiled);

    static List<MirrorAddress> AddressesIn(Dictionary<string, List<JsonObject>> sections)
    {
        var found = new Dictionary<string, (string Kind, List<MirrorUse> UsedBy)>(StringComparer.Ordinal);
        foreach (var (section, items) in sections)
        foreach (var item in items)
        {
            var noun = Kinds.All.FirstOrDefault(k => k.Section == section);
            var name = noun?.KeyOf(item) ?? "";
            Walk(item, [], (path, text) =>
            {
                foreach (Match m in Mock.Matches(text))
                {
                    var host = m.Groups["host"].Value;
                    var kind = KindOf(m.Groups["before"].Value, path);
                    var entry = found.TryGetValue(host, out var e) ? e : found[host] = (kind, new List<MirrorUse>());
                    if (entry.Kind == "host" && kind != "host") found[host] = entry = (kind, entry.UsedBy);
                    var use = new MirrorUse(section, noun?.Noun ?? section, name, string.Join(" · ", path.Where(p => p != "properties")));
                    if (!entry.UsedBy.Contains(use)) entry.UsedBy.Add(use);
                }
            });
        }
        return found.OrderBy(f => f.Value.Kind, StringComparer.Ordinal).ThenBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new MirrorAddress(f.Key, f.Value.Kind,
                f.Value.UsedBy.OrderBy(u => u.Noun, StringComparer.Ordinal).ThenBy(u => u.Key, StringComparer.Ordinal).ThenBy(u => u.Where, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    static string KindOf(string before, IReadOnlyList<string> path)
    {
        if (before == "@") return "email";
        if (before.Contains("://")) return before[..before.IndexOf("://", StringComparison.Ordinal)].ToLowerInvariant();
        var key = path.LastOrDefault()?.ToLowerInvariant() ?? "";
        return key.Contains("sftp") ? "sftp" : key.Contains("ftp") ? "ftp" : key.Contains("smtp") ? "smtp"
            : key.Contains("imap") ? "imap" : key.Contains("connection") ? "database" : "host";
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
