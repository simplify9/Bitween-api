extern alias BouncyCastle2;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CommandLine;
using BouncyCastle2::Org.BouncyCastle.Crypto;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using BouncyCastle2::Org.BouncyCastle.Crypto.Signers;
using BouncyCastle2::Org.BouncyCastle.OpenSsl;
using BouncyCastle2::Org.BouncyCastle.X509;

namespace SW.Bitween.Cli;

[Verb("login", HelpText = "Keep a publish token for a Bitween Registry, read from standard input, and where your signing key is.")]
public class RegistryLoginOptions
{
    [Value(0, Required = true, MetaName = "url", HelpText = "The registry's address, as https://registry.bitween.io")]
    public string Url { get; set; }

    [Option("key-file", HelpText = "Your publisher's Ed25519 private key, in PEM. Only its path is kept.")]
    public string KeyFile { get; set; }
}

[Verb("logout", HelpText = "Forget the registry token kept by bitween registry login.")]
public class RegistryLogoutOptions;

[Verb("whoami", HelpText = "Which registry, and which token and publisher it is.")]
public class RegistryWhoAmIOptions;

[Verb("publish", HelpText = "Sign a package or a kit with your publisher key and send it to the registry, where it waits for review.")]
public class RegistryPublishOptions
{
    [Value(0, Required = true, MetaName = "package", HelpText = "The zip bitween adapter build or bitween kit build wrote.")]
    public string Package { get; set; }

    [Option("key-file", HelpText = "The signing key, if not the one login kept (or BITWEEN_SIGNING_KEY, its PEM text, or BITWEEN_SIGNING_KEY_FILE).")]
    public string KeyFile { get; set; }
}

[Verb("status", HelpText = "Where your versions are: in review, published, rejected; with a version, its checks and the review's notes.")]
public class RegistryStatusOptions
{
    [Value(0, Required = true, MetaName = "id", HelpText = "The package's id.")]
    public string Id { get; set; }

    [Value(1, MetaName = "version", HelpText = "One version, for its checks and review.")]
    public string Version { get; set; }
}

[Verb("bundle", HelpText = "Download packages with both signatures, the registry's keys, its revocation list and your licences as one zip, for a Bitween that can't reach the registry.")]
public class RegistryBundleOptions
{
    [Value(0, Required = true, Min = 1, MetaName = "packages", HelpText = "Package ids, each with an optional @version: s9.openapi@1.2.0 s9.soap. The newest published version when none.")]
    public IEnumerable<string> Packages { get; set; }

    [Option('o', "out", HelpText = "Where to write the zip; bitween-bundle-<date>.zip in the current directory by default.")]
    public string Out { get; set; }

    [Option("url", HelpText = "The registry, when not signed in to one; https://registry.bitween.io by default.")]
    public string Url { get; set; }
}

[Verb("help-registry", Hidden = true)]
public class RegistryPlaceholderOptions;

/// <summary>
/// The registry's publish API, as a publisher's token sees it. BITWEEN_REGISTRY_URL and
/// BITWEEN_REGISTRY_TOKEN, for CI, win over what login kept.
/// </summary>
internal sealed class RegistryApi : IDisposable
{
    /// <summary>For tests: the handler every call goes through.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride;

    readonly HttpClient http;
    public string Url { get; }

    RegistryApi(string url, string token)
    {
        Url = url.Trim().TrimEnd('/');
        http = new HttpClient(HandlerOverride?.Invoke() ?? new HttpClientHandler()) { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    public const string OfficialUrl = "https://registry.bitween.io";

    public static RegistryApi For(string url, string token) => new(url, token);

    /// <summary>
    /// For reading: the registry given, else the environment's, else the one login kept, else
    /// Simplify9's; with the token when there is one, so a signed-in reader gets what it is entitled to.
    /// </summary>
    public static RegistryApi ForReading(Profiles profiles, string url)
    {
        var kept = profiles.Load().Registry;
        var envUrl = Environment.GetEnvironmentVariable("BITWEEN_REGISTRY_URL");
        var chosen = !string.IsNullOrWhiteSpace(url) ? url : !string.IsNullOrWhiteSpace(envUrl) ? envUrl : kept?.Url ?? OfficialUrl;
        // A token kept for another registry isn't sent to this one.
        var token = Environment.GetEnvironmentVariable("BITWEEN_REGISTRY_TOKEN") is { Length: > 0 } t ? t
            : string.Equals(kept?.Url?.TrimEnd('/'), chosen.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? kept?.Token : null;
        return new RegistryApi(chosen, token);
    }

    /// <summary>A file the registry builds, as its bytes; on a refusal, its words.</summary>
    public async Task<(HttpStatusCode Status, byte[] Bytes, JsonNode Body)> DownloadAsync(string path)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(Url + path);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BitweenApiException($"The registry at {Url} can't be reached: {ex.Message}");
        }
        using (response)
        {
            if (response.IsSuccessStatusCode) return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(), null);
            var text = await response.Content.ReadAsStringAsync();
            JsonNode body = null;
            try { body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
            catch (System.Text.Json.JsonException) { /* said by status */ }
            return (response.StatusCode, null, body);
        }
    }

    /// <summary>From the environment, then what login kept; null, after saying why, when neither has one.</summary>
    public static RegistryApi From(Profiles profiles)
    {
        var kept = profiles.Load().Registry;
        var url = Environment.GetEnvironmentVariable("BITWEEN_REGISTRY_URL") is { Length: > 0 } u ? u : kept?.Url;
        var token = Environment.GetEnvironmentVariable("BITWEEN_REGISTRY_TOKEN") is { Length: > 0 } t ? t : kept?.Token;
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(token)) return new RegistryApi(url, token);
        Console.WriteLine("Not signed in to a registry: bitween registry login <url>, or set BITWEEN_REGISTRY_URL and BITWEEN_REGISTRY_TOKEN.");
        return null;
    }

    public async Task<(HttpStatusCode Status, JsonNode Body)> SendAsync(HttpMethod method, string path, HttpContent content = null)
    {
        using var request = new HttpRequestMessage(method, Url + path) { Content = content };
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BitweenApiException($"The registry at {Url} can't be reached: {ex.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            JsonNode body = null;
            try { body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
            catch (System.Text.Json.JsonException) { /* not JSON: said by status below */ }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new BitweenApiException("The registry refused the token: it is unknown or revoked. bitween registry login again with a new one.", response.StatusCode);
            return (response.StatusCode, body);
        }
    }

    /// <summary>The registry's own words for a refusal: its code and message, or the status.</summary>
    public static string Said(HttpStatusCode status, JsonNode body) =>
        body?["message"] is { } message ? $"{(string)message} ({(string)body["code"]})" : $"The registry answered {(int)status}.";

    public void Dispose() => http.Dispose();
}

/// <summary>Signing as the registry checks it: Ed25519 over plain lines, the key named by its id.</summary>
internal static class PublisherKey
{
    /// <summary>
    /// The key from --key-file, BITWEEN_SIGNING_KEY (its PEM text, as a CI secret), BITWEEN_SIGNING_KEY_FILE,
    /// or the path login kept, in that order.
    /// </summary>
    public static Ed25519PrivateKeyParameters Find(string keyFile, Profiles profiles)
    {
        if (!string.IsNullOrWhiteSpace(keyFile)) return Read(File.ReadAllText(keyFile), keyFile);
        if (Environment.GetEnvironmentVariable("BITWEEN_SIGNING_KEY") is { Length: > 0 } text) return Read(text, "BITWEEN_SIGNING_KEY");
        var path = Environment.GetEnvironmentVariable("BITWEEN_SIGNING_KEY_FILE") is { Length: > 0 } p ? p : profiles.Load().Registry?.KeyFile;
        if (string.IsNullOrWhiteSpace(path))
            throw new BitweenApiException("No signing key: give --key-file, set BITWEEN_SIGNING_KEY or BITWEEN_SIGNING_KEY_FILE, or bitween registry login --key-file.");
        if (!File.Exists(path)) throw new BitweenApiException($"There's no signing key at {path}.");
        return Read(File.ReadAllText(path), path);
    }

    public static Ed25519PrivateKeyParameters Read(string pem, string from)
    {
        object read;
        try
        {
            read = new PemReader(new StringReader(pem)).ReadObject();
        }
        catch (Exception ex) when (ex is IOException or PemException or ArgumentException or InvalidCastException)
        {
            // The key's text is never repeated, only where it came from.
            throw new BitweenApiException($"The signing key in {from} can't be read: give an unencrypted Ed25519 private key in PEM.");
        }
        return read switch
        {
            Ed25519PrivateKeyParameters key => key,
            AsymmetricCipherKeyPair { Private: Ed25519PrivateKeyParameters key } => key,
            _ => throw new BitweenApiException($"The signing key in {from} isn't an Ed25519 private key in PEM."),
        };
    }

    /// <summary>The first 16 hex characters of the SHA-256 of the public key's DER, as the registry names keys.</summary>
    public static string KeyId(Ed25519PrivateKeyParameters key) =>
        Convert.ToHexStringLower(SHA256.HashData(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key.GeneratePublicKey()).GetDerEncoded()))[..16];

    public static string PublishMessage(string id, string version, string sha256) =>
        $"bitween-registry/v1/publish\n{id}\n{version}\n{sha256.ToLowerInvariant()}";

    public static string Sign(Ed25519PrivateKeyParameters key, string message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        var bytes = Encoding.UTF8.GetBytes(message);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }
}

public static partial class Commands
{
    /// <summary>Exit code of bitween registry publish when the version is already in the registry.</summary>
    public const int AlreadyPublished = 2;

    internal static async Task<int> RegistryLogin(RegistryLoginOptions opts, Profiles profiles, TextReader input)
    {
        var token = Environment.GetEnvironmentVariable("BITWEEN_REGISTRY_TOKEN") is { Length: > 0 } t ? t : input.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("Give the publish token on standard input, as: echo $TOKEN | bitween registry login <url>");
            return Failure;
        }

        string keyFile = null;
        if (!string.IsNullOrWhiteSpace(opts.KeyFile))
        {
            keyFile = Path.GetFullPath(opts.KeyFile);
            if (!File.Exists(keyFile))
            {
                Console.WriteLine($"There's no signing key at {keyFile}.");
                return Failure;
            }
            PublisherKey.Read(await File.ReadAllTextAsync(keyFile), keyFile);
        }

        using var api = RegistryApi.For(opts.Url, token);
        var (status, me) = await api.SendAsync(HttpMethod.Get, "/v1/me");
        if (status != HttpStatusCode.OK)
        {
            Console.WriteLine(RegistryApi.Said(status, me));
            return Failure;
        }

        var file = profiles.Load();
        file.Registry = new RegistryProfile { Url = api.Url, Token = token, KeyFile = keyFile };
        profiles.Save(file);
        Console.WriteLine($"Signed in to {api.Url} with the token {(string)me?["name"]}, for the publisher {(string)me?["publisher"] ?? "(none)"}.");
        if (keyFile != null)
        {
            var key = PublisherKey.Read(await File.ReadAllTextAsync(keyFile), keyFile);
            Console.WriteLine($"Signing with the key {PublisherKey.KeyId(key)} in {keyFile}.");
        }
        return Success;
    }

    internal static Task<int> RegistryLogout(Profiles profiles)
    {
        var file = profiles.Load();
        if (file.Registry == null)
        {
            Console.WriteLine("Not signed in to a registry.");
            return Task.FromResult(Success);
        }
        var url = file.Registry.Url;
        file.Registry = null;
        profiles.Save(file);
        Console.WriteLine($"Forgot the token for {url}.");
        return Task.FromResult(Success);
    }

    internal static async Task<int> RegistryWhoAmI(Profiles profiles)
    {
        using var api = RegistryApi.From(profiles);
        if (api == null) return Failure;
        var (status, me) = await api.SendAsync(HttpMethod.Get, "/v1/me");
        if (status != HttpStatusCode.OK)
        {
            Console.WriteLine(RegistryApi.Said(status, me));
            return Failure;
        }
        Console.WriteLine($"{api.Url}: the token {(string)me?["name"]} ({(string)me?["scope"]}), for the publisher {(string)me?["publisher"] ?? "(none)"}.");
        return Success;
    }

    internal static async Task<int> RegistryPublish(RegistryPublishOptions opts, Profiles profiles)
    {
        if (!File.Exists(opts.Package))
        {
            Console.WriteLine($"There is no package at {opts.Package}; bitween adapter build or bitween kit build makes one.");
            return Failure;
        }

        var bytes = await File.ReadAllBytesAsync(opts.Package);
        string id, version;
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var entry = zip.GetEntry("adapter.json");
            if (entry == null)
            {
                Console.WriteLine($"{opts.Package} has no adapter.json, so it isn't a package bitween adapter build or kit build wrote.");
                return Failure;
            }
            using var reader = new StreamReader(entry.Open());
            var manifest = JsonNode.Parse(await reader.ReadToEndAsync());
            id = ((string)manifest?["id"])?.Trim().ToLowerInvariant();
            version = ((string)manifest?["version"])?.Trim();
        }
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            Console.WriteLine($"{opts.Package}: its adapter.json has no id or version.");
            return Failure;
        }

        var key = PublisherKey.Find(opts.KeyFile, profiles);
        using var api = RegistryApi.From(profiles);
        if (api == null) return Failure;

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var keyId = PublisherKey.KeyId(key);
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } }, "package", Path.GetFileName(opts.Package) },
            { new StringContent(keyId), "keyId" },
            { new StringContent(PublisherKey.Sign(key, PublisherKey.PublishMessage(id, version, sha256))), "signature" },
        };

        var (status, body) = await api.SendAsync(HttpMethod.Post, $"/v1/packages/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(version)}", form);
        switch (status)
        {
            case HttpStatusCode.Accepted or HttpStatusCode.OK:
                Console.WriteLine($"{id} {version} is {Status(body?["status"])} at {api.Url} (sha256 {sha256}, signed with {keyId}).");
                PrintChecks(body?["checks"], failedOnly: false);
                Console.WriteLine($"bitween registry status {id} {version} shows where it is.");
                return Success;
            case HttpStatusCode.UnprocessableEntity:
                Console.WriteLine($"{id} {version} was refused:");
                PrintChecks(body?["checks"], failedOnly: true);
                return Failure;
            case HttpStatusCode.Conflict:
                Console.WriteLine($"{id} {version} is already in the registry. Bump the version and build again. {RegistryApi.Said(status, body)}");
                return AlreadyPublished;
            default:
                Console.WriteLine(RegistryApi.Said(status, body));
                return Failure;
        }
    }

    internal static async Task<int> RegistryBundle(RegistryBundleOptions opts, Profiles profiles)
    {
        var packages = string.Join(",", opts.Packages.Select(p => p.Trim()).Where(p => p.Length > 0));
        if (packages.Length == 0)
        {
            Console.WriteLine("Name at least one package: bitween registry bundle s9.openapi@1.2.0 s9.soap");
            return Failure;
        }
        using var api = RegistryApi.ForReading(profiles, opts.Url);
        var (status, bytes, body) = await api.DownloadAsync("/v1/bundle?packages=" + Uri.EscapeDataString(packages));
        if (bytes == null)
        {
            Console.WriteLine(RegistryApi.Said(status, body));
            return Failure;
        }
        var path = string.IsNullOrWhiteSpace(opts.Out) ? $"bitween-bundle-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip" : opts.Out;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllBytesAsync(path, bytes);
        var count = 0;
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
            count = zip.Entries.Count(e => e.FullName.StartsWith("packages/", StringComparison.Ordinal));
        Console.WriteLine($"Wrote {path}: {count} package(s) from {api.Url}, {bytes.Length / 1024} KB, with the keys, the revocation list and the licences.");
        Console.WriteLine("On the Bitween that can't reach the registry: Adapters, Marketplace, Install from bundle.");
        return Success;
    }

    internal static async Task<int> RegistryStatus(RegistryStatusOptions opts, Profiles profiles)
    {
        using var api = RegistryApi.From(profiles);
        if (api == null) return Failure;
        var id = opts.Id.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(opts.Version))
        {
            var (status, mine) = await api.SendAsync(HttpMethod.Get, "/v1/mine/packages");
            if (status != HttpStatusCode.OK)
            {
                Console.WriteLine(RegistryApi.Said(status, mine));
                return Failure;
            }
            var package = mine?.AsArray().FirstOrDefault(p => (string)p?["id"] == id);
            if (package == null)
            {
                Console.WriteLine($"{id} isn't one of your packages at {api.Url}.");
                return Failure;
            }
            Console.WriteLine($"{id} ({(string)package["displayName"]}), {(string)package["visibility"]}, {(string)package["pricing"]}:");
            foreach (var v in package["versions"]?.AsArray() ?? [])
            {
                var revoked = (string)v?["revokeReason"] is { Length: > 0 } reason ? $": {reason}" : "";
                Console.WriteLine($"  {(string)v?["version"],-12} {Status(v?["status"])}{revoked}");
            }
            return Success;
        }

        var (s, detail) = await api.SendAsync(HttpMethod.Get, $"/v1/mine/packages/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(opts.Version.Trim())}");
        if (s != HttpStatusCode.OK)
        {
            Console.WriteLine(RegistryApi.Said(s, detail));
            return Failure;
        }
        Console.WriteLine($"{id} {opts.Version.Trim()} is {Status(detail?["version"]?["status"])}.");
        PrintChecks(detail?["checks"], failedOnly: false);
        if ((string)detail?["reviewedBy"] is { Length: > 0 } by)
            Console.WriteLine($"Reviewed by {by}{((string)detail["reviewNotes"] is { Length: > 0 } notes ? $": {notes}" : ".")}");
        return Success;
    }

    /// <summary>A status as the registry names it, InReview read as "in review".</summary>
    static string Status(JsonNode status) => status?.ToString() switch
    {
        null => "sent",
        "InReview" => "in review",
        var s => s.ToLowerInvariant(),
    };

    static void PrintChecks(JsonNode checks, bool failedOnly)
    {
        foreach (var c in checks?.AsArray() ?? [])
        {
            var outcome = c?["outcome"]?.ToString();
            if (failedOnly && outcome != "Failed") continue;
            var detail = (string)c?["detail"] is { Length: > 0 } d ? $": {d}" : "";
            var label = outcome switch { "Failed" => "FAIL", "Passed" => "pass", "Skipped" => "skip", _ => (outcome ?? "").ToLowerInvariant() };
            Console.WriteLine($"  {label,-5} {(string)c?["name"]}{detail}");
        }
    }
}
