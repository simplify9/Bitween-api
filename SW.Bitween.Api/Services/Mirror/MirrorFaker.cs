using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Turns prod's values into stand-ins of the same shape, so a staging copy behaves like prod without
/// holding anything of prod's: ten digits stay ten digits, a date stays a date in its format, a GUID
/// stays a GUID, and an address becomes a mock address no network can reach. The same value always
/// becomes the same stand-in under one key, so a code named in two places still matches, and copying
/// again changes nothing.
/// </summary>
/// <param name="keep">
/// Words staging's own copy of prod's setup already names - a filter's 'SHIPPED', a file pattern's ORD -
/// kept as they are wherever they turn up, so a faked value takes the path the real one took. Keeping
/// one tells staging nothing it doesn't hold already.
/// </param>
public sealed class MirrorFaker(byte[] key, IEnumerable<string> keep = null)
{
    readonly HashSet<string> keep = new(keep ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>Where every mock address lives: a name reserved never to resolve (RFC 2606).</summary>
    public const string MockDomain = "mirror.invalid";

    /// <summary>What Bitween answers in place of a secret it holds; staging never sees what it stands for.</summary>
    public const string Masked = "__private__";

    // ------------------------------------------------------------------ values in context

    /// <summary>
    /// What a setting or a value of prod's is that staging never holds, so a copy leaves it empty for
    /// staging's own: an address (a URL, a host, an email, a connection string), a secret (one prod
    /// masks, one named like a secret, or one that reads as a credential whatever its name) or a login
    /// (one named like a user name). Null for anything
    /// else. A host built from a {{token}} isn't one yet, nor is a secret or a login that names one: the
    /// token is what is copied. Hosts every system names alike, such as an XML namespace's www.w3.org,
    /// are nobody's.
    /// </summary>
    public static string BlankKind(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value == Masked || IsSecretLike(key) && !TokenPattern.IsMatch(value) || LooksLikeCredential(value)) return "secret";
        if (IsLoginKey(key) && !TokenPattern.IsMatch(value)) return "login";
        if (IsConnectionStringKey(key) || LooksLikeConnectionString(value) || HostsIn(key, value).Any(h => !Untouched(h))) return "address";
        return null;
    }

    /// <summary>Where a placeholder address leads: a name reserved never to resolve (RFC 2606).</summary>
    public const string PlaceholderHost = "set-me.invalid";

    /// <summary>A placeholder login.</summary>
    public const string PlaceholderText = "set-me";

    /// <summary>
    /// What stands in for one of prod's addresses or secrets until staging's own is filled in. Bitween
    /// won't save some items without an address or a login, so those get a placeholder that leads
    /// nowhere; a secret stays empty, since Bitween hides one once it is set.
    /// </summary>
    public static string Placeholder(string kind, string key, string value)
    {
        if (kind == "secret") return "";
        if (kind == "login") return PlaceholderText;
        var trimmed = value.Trim();
        if (UrlPattern.Match(trimmed) is { Success: true } url && url.Index == 0) return $"{url.Groups["scheme"].Value}://{PlaceholderHost}";
        if (EmailPattern.IsMatch(trimmed)) return $"{PlaceholderText}@{PlaceholderHost}";
        if (IsConnectionStringKey(key) || LooksLikeConnectionString(trimmed)) return $"Host={PlaceholderHost}";
        return PlaceholderHost;
    }

    /// <summary>Whether a value of staging's is still what the copy left: empty, or a placeholder.</summary>
    public static bool IsPlaceholder(string value) =>
        string.IsNullOrEmpty(value) || string.Equals(value.Trim(), PlaceholderText, StringComparison.OrdinalIgnoreCase)
        || value.Contains(PlaceholderHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A partner's property or a global value that is neither an address nor a secret: someone's data,
    /// so it is faked, unless it was asked to be kept because it is a setting rather than data (a time
    /// zone, a date format). A port is nobody's data.
    /// </summary>
    public string Datum(string key, string value, bool keepReal = false) =>
        string.IsNullOrEmpty(value) || keepReal || IsPortKey(key) ? value : Fake(value);

    /// <summary>
    /// A name a secret goes by, declared one or not: such a value is never copied as it is, whatever
    /// the list of values to keep says.
    /// </summary>
    public static bool IsSecretLike(string key)
    {
        var k = (key ?? "").ToLowerInvariant().Replace("_", "").Replace("-", "");
        return k is "pass" or "key" or "pin" || SecretWords.Any(k.Contains);
    }

    /// <summary>
    /// A value that reads as a credential whatever its name: a JWT, a bearer or basic header, or a long run
    /// of letters and digits with no word in it, such as a license or access key. Prod shows a secret as
    /// it is when its adapter doesn't say it is one, and a name like TraxisJwt or LicenseKey says nothing.
    /// A GUID or a path isn't one.
    /// </summary>
    public static bool LooksLikeCredential(string value)
    {
        var v = (value ?? "").Trim();
        if (JwtPattern.IsMatch(v) || AuthHeaderPattern.IsMatch(v)) return true;
        if (v.Length < 20 || v.StartsWith('/') || System.Guid.TryParse(v, out _) || !KeyPattern.IsMatch(v)) return false;
        var kinds = (v.Any(char.IsUpper) ? 1 : 0) + (v.Any(char.IsLower) ? 1 : 0) + (v.Any(char.IsDigit) ? 1 : 0);
        return kinds >= 2 && BitsPerChar(v) >= 4.0;
    }

    /// <summary>How unpredictable a value's characters are: about 4.3 for a random key of 20, under 4 for words and paths.</summary>
    static double BitsPerChar(string v) =>
        -v.GroupBy(c => c).Sum(g => (double)g.Count() / v.Length * Math.Log2((double)g.Count() / v.Length));

    /// <summary>
    /// A name a login goes by - a user name, a client id - which prod's never is on
    /// staging: the team's test login is. SftpUsername and ftpUser are; UserAgent isn't.
    /// </summary>
    public static bool IsLoginKey(string key)
    {
        var k = (key ?? "").ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(".", "");
        return LoginEndings.Any(k.EndsWith);
    }

    /// <summary>How every stand-in secret starts, which tells one from a secret set on staging.</summary>
    public const string SecretPrefix = "mirror-";

    /// <summary>A stand-in for a secret Bitween wouldn't show: the same each time for the same place.</summary>
    public string Secret(string where)
    {
        var bytes = Bytes("secret", where ?? "", 20);
        var text = new StringBuilder(SecretPrefix);
        foreach (var b in bytes) text.Append(Base36[b % Base36.Length]);
        return text.ToString();
    }

    // ------------------------------------------------------------------ shape

    /// <summary>
    /// The value with every letter and digit replaced, and everything else kept: punctuation, spaces,
    /// case, length, {{tokens}}. JSON keeps its keys and structure; dates stay valid dates in their
    /// format; GUIDs and hex stay hex; addresses inside become mock ones.
    /// </summary>
    public string Fake(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var trimmed = value.Trim();
        if (Kept(trimmed)) return value;
        if (trimmed.Length > 1 && trimmed[0] is '{' or '[' && Json(trimmed) is { } json) return json;

        var text = new StringBuilder(value.Length);
        var at = 0;
        foreach (var (start, length, kind) in Segments(value))
        {
            if (start > at) text.Append(Plain(value[at..start]));
            var part = value.Substring(start, length);
            text.Append(kind switch
            {
                Segment.Token => part,
                Segment.Url => Url(part, fakeQuery: true),
                _ => Email(part),
            });
            at = start + length;
        }
        if (at < value.Length) text.Append(Plain(value[at..]));
        return text.ToString();
    }

    string Plain(string text)
    {
        if (text.Length == 0 || Kept(text.Trim())) return text;
        if (IsoDate.Match(text) is { Success: true } date) return Date(text, date);
        if (Hex.IsMatch(text) || Guid.IsMatch(text)) return HexShape(text);
        return Words(text);
    }

    /// <summary>
    /// Each word on its own, so a word is the same stand-in wherever it stands - alone, in a sentence
    /// or in a file name: one the setup names is kept, a date or a GUID keeps its form, the rest is
    /// shaped. What is between words stays.
    /// </summary>
    string Words(string text)
    {
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (Match m in WordPattern.Matches(text))
        {
            result.Append(text, at, m.Index - at);
            var word = m.Value;
            result.Append(
                Kept(word) ? word
                : m.Groups["date"].Success ? Date(word, IsoDate.Match(word))
                : m.Groups["guid"].Success || Hex.IsMatch(word) ? HexShape(word)
                : CompactDate(word) ?? Shape(word));
            at = m.Index + m.Length;
        }
        result.Append(text, at, text.Length - at);
        return result.ToString();
    }

    /// <summary>
    /// Letters stay letters of the same case, digits stay digits of the same script; a number keeps a
    /// leading digit that isn't 0.
    /// </summary>
    string Shape(string text)
    {
        var bytes = Bytes("shape", text, text.Length);
        var result = new char[text.Length];
        // Still in a number's leading zeros, which a padded number keeps, or at its first other digit.
        var leading = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var b = bytes[i];
            if (i == 0 || !char.IsDigit(text[i - 1])) leading = true;
            if (char.IsDigit(c))
            {
                // Any script's digits - Arabic-Indic ones too - counted from that script's zero.
                var value = CharUnicodeInfo.GetDigitValue(c);
                var zero = (char)(c - value);
                result[i] = value == 0 && leading ? c
                    : leading ? (char)(zero + 1 + b % 9)
                    : (char)(zero + b % 10);
                if (value != 0) leading = false;
                continue;
            }
            result[i] = c switch
            {
                _ when char.IsAsciiLetterUpper(c) => (char)('A' + b % 26),
                _ when char.IsAsciiLetterLower(c) => (char)('a' + b % 26),
                // Another script's letters, which have no case to keep: a letter all the same.
                _ when char.IsLetter(c) => (char)('a' + b % 26),
                _ => c,
            };
        }
        return new string(result);
    }

    /// <summary>
    /// yyyyMMdd, with the time after it or not, as file names and fixed-width records write a date:
    /// moved as the same day written 2026-10-20 is, so both forms stay the same day. Null when it isn't one.
    /// </summary>
    string CompactDate(string word)
    {
        if (word.Length is not (8 or 12 or 14) || !word.All(char.IsAsciiDigit)) return null;
        if (!DateTime.TryParseExact(word[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            day.Year is < 1990 or > 2099)
            return null;
        return Shift(day).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + word[8..];
    }

    /// <summary>A file's name: faked as any text is, with its extension kept, so ORD_*.csv still matches when the setup names ORD.</summary>
    public string FileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? Plain(name[..dot]) + name[dot..] : Plain(name);
    }

    bool Kept(string word) => KeptWords.Contains(word) || keep.Contains(word);

    string Digits(string number)
    {
        var bytes = Bytes("digits", number, number.Length);
        return new string(number.Select((c, i) => !char.IsAsciiDigit(c) ? c
            : i == 0 || !char.IsAsciiDigit(number[i - 1]) ? c == '0' ? '0' : (char)('1' + bytes[i] % 9)
            : (char)('0' + bytes[i] % 10)).ToArray());
    }

    /// <summary>Hex stays hex, in its case; separators and braces stay where they are.</summary>
    string HexShape(string text)
    {
        var bytes = Bytes("hex", text, text.Length);
        var result = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            result[i] = char.IsAsciiHexDigit(c)
                ? (char.IsAsciiLetterUpper(c) ? "0123456789ABCDEF" : "0123456789abcdef")[bytes[i] % 16]
                : c;
        }
        return new string(result);
    }

    /// <summary>A date moved by up to a year either way, written as it was; the time of day is kept.</summary>
    string Date(string text, Match match)
    {
        if (!DateTime.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Shape(text);
        var rest = text[match.Length..];
        return Shift(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (TimeOfDay.IsMatch(rest) ? rest : Shape(rest));
    }

    /// <summary>A day moved by up to a year either way: the same day always by the same amount.</summary>
    DateTime Shift(DateTime day) =>
        day.AddDays(BitConverter.ToUInt16(Bytes("date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 2)) % 731 - 365);

    /// <summary>A JSON document faked as a value holding one is; null when the text isn't JSON.</summary>
    public string FakeJson(string text) => Json(text.Trim());

    /// <summary>A number's digits faked, its sign, point and exponent kept, as a spreadsheet holds one.</summary>
    public string Number(string number) => string.IsNullOrEmpty(number) || Kept(number) ? number : Digits(number);

    string Json(string text)
    {
        try
        {
            var node = JsonNode.Parse(text);
            return node == null ? null : FakeNode(node)?.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = text.Contains('\n'),
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }
        // Not JSON after all, or JSON naming a key twice, which a JsonObject can't hold: read as text.
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    JsonNode FakeNode(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, FakeNode(p.Value)))),
        JsonArray list => new JsonArray(list.Select(FakeNode).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Fake(s)),
        JsonValue v when v.TryGetValue<bool>(out _) => v.DeepClone(),
        // A number: its digits only, so 1e5 stays a number.
        JsonValue v => JsonNode.Parse(Digits(v.ToJsonString())),
        _ => null,
    };

    // ------------------------------------------------------------------ addresses

    /// <summary>Every address in the text made a mock one; the rest of the text as it was.</summary>
    public string Addresses(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = new StringBuilder(text.Length);
        var at = 0;
        foreach (var (start, length, kind) in Segments(text).Where(s => s.Kind != Segment.Token))
        {
            result.Append(text, at, start - at);
            var part = text.Substring(start, length);
            result.Append(kind == Segment.Url ? Url(part, fakeQuery: false) : Email(part));
            at = start + length;
        }
        result.Append(text, at, text.Length - at);
        return result.ToString();
    }

    /// <summary>A host's mock: the same each time. Loopback and names that resolve nowhere are left alone.</summary>
    public string Host(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Contains("{{")) return host;
        var lower = host.Trim('[', ']').ToLowerInvariant();
        if (IPAddress.TryParse(lower, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return host;
            var n = 1 + Bytes("ip", lower, 1)[0] % 254;
            // Addresses kept for documentation, which route nowhere (RFC 5737, RFC 3849).
            return ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[2001:db8::{n:x}]" : $"192.0.2.{n}";
        }
        if (Untouched(lower)) return host;
        var hash = Bytes("host", lower, 4);
        return $"m{Convert.ToHexString(hash).ToLowerInvariant()}.{MockDomain}";
    }

    string Url(string url, bool fakeQuery)
    {
        var m = UrlPattern.Match(url);
        if (!m.Success) return url;
        var authority = m.Groups["auth"].Value;
        if (authority.Contains("{{")) return url;

        var user = "";
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var info = authority[..at];
            var colon = info.IndexOf(':');
            // Who signs in and with what are prod's: the user faked, the password a stand-in of its shape.
            user = (colon < 0 ? Fake(info) : Fake(info[..colon]) + ":" + Shape(info[(colon + 1)..])) + "@";
            authority = authority[(at + 1)..];
        }
        var (host, port) = SplitPort(authority);
        if (Untouched(host.ToLowerInvariant()) && user.Length == 0) return url;
        var rest = m.Groups["rest"].Value;
        return $"{m.Groups["scheme"].Value}://{user}{Host(host)}{port}{Query(rest, fakeQuery)}";
    }

    /// <summary>The path as it is; the query's values faked, where keys and tokens like ?apikey=... keep their place.</summary>
    string Query(string rest, bool fake)
    {
        var q = rest.IndexOf('?');
        if (q < 0 || !fake) return rest;
        var hash = rest.IndexOf('#', q);
        var query = hash < 0 ? rest[(q + 1)..] : rest[(q + 1)..hash];
        var faked = string.Join("&", query.Split('&').Select(pair =>
        {
            var eq = pair.IndexOf('=');
            return eq < 0 ? pair : pair[..(eq + 1)] + Fake(pair[(eq + 1)..]);
        }));
        return rest[..(q + 1)] + faked + (hash < 0 ? "" : rest[hash..]);
    }

    string Email(string email)
    {
        var at = email.LastIndexOf('@');
        var domain = email[(at + 1)..];
        if (Untouched(domain.ToLowerInvariant())) return email;
        return Shape(email[..at]) + "@" + Host(domain);
    }

    static (string Host, string Port) SplitPort(string authority)
    {
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            return close < 0 ? (authority, "") : (authority[..(close + 1)], authority[(close + 1)..]);
        }
        var colon = authority.LastIndexOf(':');
        return colon > 0 && authority.IndexOf(':') == colon && authority[(colon + 1)..].All(char.IsAsciiDigit)
            ? (authority[..colon], authority[colon..])
            : (authority, "");
    }

    /// <summary>
    /// Hosts no call goes to on purpose: loopback, names reserved for tests, and the schema hosts XML
    /// and JSON name their namespaces after - a namespace is matched as text, so changing it would
    /// change what a flow does.
    /// </summary>
    static bool Untouched(string host) =>
        host is "localhost" or "w3.org" or "www.w3.org" or "schemas.xmlsoap.org" or "schemas.microsoft.com" or "purl.org"
            or "json-schema.org" or "schemas.openxmlformats.org" or "tempuri.org" or "schema.org"
        || host.EndsWith("." + MockDomain, StringComparison.Ordinal) || host.EndsWith(".invalid", StringComparison.Ordinal)
        || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".test", StringComparison.Ordinal)
        || host.EndsWith(".example", StringComparison.Ordinal) || host is "example.com" or "example.org" or "example.net";

    // ------------------------------------------------------------------ reading values

    enum Segment { Token, Url, Email }

    /// <summary>Tokens, URLs and emails in the text, in order and apart: what isn't plain text.</summary>
    static IEnumerable<(int Start, int Length, Segment Kind)> Segments(string text)
    {
        var found = new List<(int Start, int Length, Segment Kind)>();
        foreach (Match m in TokenPattern.Matches(text)) found.Add((m.Index, m.Length, Segment.Token));
        foreach (Match m in UrlPattern.Matches(text)) found.Add((m.Index, m.Length, Segment.Url));
        foreach (Match m in EmailPattern.Matches(text)) found.Add((m.Index, m.Length, Segment.Email));
        var end = 0;
        foreach (var s in found.OrderBy(s => s.Start).ThenBy(s => s.Kind))
        {
            if (s.Start < end) continue;
            yield return s;
            end = s.Start + s.Length;
        }
    }

    public static bool IsAddressKey(string key)
    {
        var k = (key ?? "").ToLowerInvariant();
        return AddressWords.Any(k.Contains);
    }

    /// <summary>
    /// Every host a setting names, in lower case and without its port: in URLs and email addresses
    /// anywhere in it, by the server a connection string names, and as a bare host, IP or list of them
    /// where the setting is named for an address. A host built from a {{token}} isn't one yet.
    /// </summary>
    public static IEnumerable<string> HostsIn(string key, string value) => AllHostsIn(key, value).Distinct();

    static IEnumerable<string> AllHostsIn(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) yield break;
        foreach (Match m in UrlPattern.Matches(value))
        {
            var authority = m.Groups["auth"].Value;
            if (authority.Contains("{{")) continue;
            if (authority.LastIndexOf('@') is var at and >= 0) authority = authority[(at + 1)..];
            if (Bare(SplitPort(authority).Host) is { } host) yield return host;
        }
        foreach (Match m in EmailPattern.Matches(value))
            yield return m.Value[(m.Value.LastIndexOf('@') + 1)..].ToLowerInvariant();
        if (value.Contains("://")) yield break;

        IEnumerable<string> parts =
            LooksLikeConnectionString(value) ? value.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && ConnectionHostKeys.Contains(p[0].Trim().ToLowerInvariant().Replace(" ", ""))).SelectMany(p => p[1].Split(','))
            : IsAddressKey(key) ? value.Split(',', ';')
            : [];
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) p = p[4..];
            if (Bare(SplitPort(p).Host) is { } host) yield return host;
        }

        static string Bare(string host)
        {
            var h = host.Trim().Trim('[', ']').ToLowerInvariant();
            return h.Length > 0 && !h.Contains("{{") && (IsHost(h) || IsIp(h)) ? h : null;
        }

        // Written out as an address is: "30" parses as 0.0.0.30, and is a number of seconds.
        static bool IsIp(string h)
        {
            if (!IPAddress.TryParse(h, out var ip)) return false;
            return ip.AddressFamily == AddressFamily.InterNetworkV6 ? h.Contains(':') : h.Count(c => c == '.') == 3;
        }
    }

    static readonly HashSet<string> ConnectionHostKeys =
        ["host", "server", "datasource", "address", "addr", "networkaddress", "endpoint", "hostname"];

    static bool IsPortKey(string key) => (key ?? "").EndsWith("port", StringComparison.OrdinalIgnoreCase);

    static bool IsConnectionStringKey(string key)
    {
        var k = (key ?? "").ToLowerInvariant().Replace("_", "").Replace("-", "");
        return k.Contains("connectionstring") || k.Contains("connstr") || k == "dsn";
    }

    /// <summary>name=value; pairs, one of them naming a server: Host=db;Port=5432;Password=...</summary>
    static bool LooksLikeConnectionString(string value) =>
        value.Contains('=') && value.Contains(';') &&
        ConnectionHostPattern.IsMatch(value);

    static bool IsHost(string value) => HostPattern.IsMatch(value) && !value.Contains("{{");

    // Deterministic bytes for a purpose and an input, as many as asked for.
    byte[] Bytes(string purpose, string input, int count)
    {
        var result = new byte[count];
        using var hmac = new HMACSHA256(key);
        // Only the first block reads the input; the rest grow from it. Each reading it took a long value's
        // length squared: a megabyte in a file, minutes.
        var first = Block(hmac, 0, Encoding.UTF8.GetBytes(purpose + "\0" + input));
        var filled = 0;
        for (var block = 0; filled < count; block++)
        {
            var hash = block == 0 ? first : Block(hmac, block, first);
            var take = Math.Min(hash.Length, count - filled);
            Array.Copy(hash, 0, result, filled, take);
            filled += take;
        }
        return result;
    }

    static byte[] Block(HMACSHA256 hmac, int block, byte[] data)
    {
        hmac.Initialize();
        hmac.TransformBlock(BitConverter.GetBytes(block), 0, 4, null, 0);
        hmac.TransformFinalBlock(data, 0, data.Length);
        return hmac.Hash!;
    }

    const string Base36 = "abcdefghijklmnopqrstuvwxyz0123456789";

    static readonly HashSet<string> KeptWords = new(StringComparer.OrdinalIgnoreCase) { "true", "false", "null", "none" };

    static readonly string[] SecretWords =
        ["password", "passwd", "pwd", "secret", "token", "credential", "apikey", "accesskey", "privatekey", "passphrase", "certificate", "signingkey",
         "jwt", "bearer", "licensekey"];

    static readonly string[] LoginEndings = ["user", "username", "userid", "login", "loginname", "loginid", "clientid"];

    static readonly string[] AddressWords = ["url", "uri", "host", "server", "endpoint", "address", "domain", "webhook", "smtp", "imap", "ftp"];

    static readonly Regex TokenPattern = new(@"\{\{[^}]*\}\}", RegexOptions.Compiled);
    static readonly Regex JwtPattern = new(@"^eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]*$", RegexOptions.Compiled);
    static readonly Regex AuthHeaderPattern = new(@"^(Bearer|Basic)\s+[A-Za-z0-9+/=_\-.]{16,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex KeyPattern = new(@"^[A-Za-z0-9+/=_\-]+$", RegexOptions.Compiled);
    // Scheme and local part are bounded, as real ones are: unbounded, a long value with neither :// nor @,
    // such as a file's encoded attachment, took a search from every position to its end.
    static readonly Regex UrlPattern = new(@"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]{0,31})://(?<auth>[^\s/?#""'<>\\]+)(?<rest>[^\s""'<>\\]*)", RegexOptions.Compiled);
    static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+\-]{1,64}@(?:[A-Za-z0-9\-]+\.)+[A-Za-z]{2,63}", RegexOptions.Compiled);
    static readonly Regex HostPattern = new(@"^(?:[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z][A-Za-z0-9\-]{1,62}\.?$", RegexOptions.Compiled);
    static readonly Regex ConnectionHostPattern = new(@"(^|;)\s*(host|server|data source|address|endpoint|accountname)\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}(?=$|[T ])", RegexOptions.Compiled);
    // At least one letter: digits alone are a number, and stay digits.
    static readonly Regex Hex = new(@"^(?:(?=[0-9]*[a-f])[0-9a-f]{8,}|(?=[0-9]*[A-F])[0-9A-F]{8,})$", RegexOptions.Compiled);
    static readonly Regex TimeOfDay = new(@"^([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.Compiled);
    static readonly Regex Guid = new(@"^[{(]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[)}]?$", RegexOptions.Compiled);
    // A date with its time, a GUID, or a run of letters and digits: what Words fakes one at a time.
    // Bounded by what can't continue them rather than \b, which an underscore defeats: ORD_2026-10-20.
    static readonly Regex WordPattern = new(
        @"(?<date>(?<![0-9])[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]+)?)?(?:Z|[+-][0-9]{2}:?[0-9]{2})?)?(?![0-9]))" +
        @"|(?<guid>(?<![0-9a-fA-F])[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}(?![0-9a-fA-F]))" +
        @"|[\p{L}\p{M}\p{Nd}]+",
        RegexOptions.Compiled);
}
