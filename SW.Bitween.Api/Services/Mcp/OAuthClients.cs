#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SW.Bitween.Services.Mcp;

/// <summary>An app signing in with OAuth, as it describes itself: its id, its name and where to send people back.</summary>
public record OAuthClient(string Id, string Name, IReadOnlyList<string> RedirectUris);

/// <summary>
/// Who the apps signing in with OAuth are. An app either names itself with the address of its own
/// metadata document (a Client ID Metadata Document, as Claude and ChatGPT do), which is fetched and
/// checked, or registers once and is handed a client id that carries its name and addresses, signed.
/// Neither is stored: an AI app may register afresh on every connection, and a table of them would
/// only grow.
/// </summary>
public class OAuthClients(IConfiguration configuration, IMemoryCache cache, ILogger<OAuthClients> logger)
{
    /// <summary>Client ids Bitween issued at registration start with this.</summary>
    public const string RegisteredPrefix = "bwc_";

    const int MaxDocumentBytes = 64 * 1024;
    const int MaxRedirectUris = 10;
    static readonly TimeSpan Cached = TimeSpan.FromMinutes(5);
    static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    // The address in an app's client id is the app's to choose, so the fetch goes through the
    // adapters' address guard with private addresses always refused: never this machine, the
    // network it's on, or cloud metadata.
    static readonly Lazy<HttpClient> Http = new(() => new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
        ConnectCallback = SW.Bitween.NativeAdapters.OutboundAddressGuard.ConnectCallback(blockPrivate: true),
    }) { Timeout = FetchTimeout });

    /// <summary>For tests: answers in place of the network.</summary>
    internal static Func<HttpMessageHandler>? HandlerOverride { get; set; }

    byte[]? key;

    byte[] Key => key ??= HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? throw new InvalidOperationException("Token:Key is not set.")), 32,
        info: Encoding.UTF8.GetBytes("bitween:oauth-clients:v1"));

    /// <summary>A client id for an app registering itself: its name and addresses, signed.</summary>
    /// <exception cref="ArgumentException">An address that can't be used, with the reason.</exception>
    public OAuthClient Register(string? name, IReadOnlyList<string>? redirectUris)
    {
        if (redirectUris is not { Count: > 0 } || redirectUris.Count > MaxRedirectUris)
            throw new ArgumentException($"Give between 1 and {MaxRedirectUris} redirect_uris.");
        foreach (var uri in redirectUris)
            if (RedirectProblem(uri) is { } problem) throw new ArgumentException(problem);

        var cleanName = Clean(name) ?? new Uri(redirectUris[0]).Host;
        var payload = JsonSerializer.Serialize(new Registration(cleanName, redirectUris.ToList()));
        var id = RegisteredPrefix + SignInCodes.Base64Url(Encoding.UTF8.GetBytes(payload)) + "." +
                 SignInCodes.Base64Url(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload)));
        return new OAuthClient(id, cleanName, redirectUris.ToList());
    }

    /// <summary>The app with this client id, or null when it's unknown or its metadata doesn't hold up.</summary>
    public async Task<OAuthClient?> Find(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        if (clientId.StartsWith(RegisteredPrefix, StringComparison.Ordinal)) return Registered(clientId);
        if (!clientId.StartsWith("https://", StringComparison.Ordinal)) return null;
        if (cache.TryGetValue(CacheKey(clientId), out OAuthClient? known)) return known;

        var found = await Fetch(clientId);
        if (found is not null) cache.Set(CacheKey(clientId), found, Cached);
        return found;
    }

    /// <summary>
    /// Whether an app may be sent back to this address: one it listed, exactly - except an address on
    /// this computer (127.0.0.1 or localhost over http), where a native app such as Claude Code picks a
    /// free port each time, so any port does.
    /// </summary>
    public static bool Allows(OAuthClient client, string? redirectUri)
    {
        if (string.IsNullOrEmpty(redirectUri) || RedirectProblem(redirectUri) is not null) return false;
        if (client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal)) return true;

        var asked = new Uri(redirectUri);
        return IsLoopback(asked) && client.RedirectUris.Any(listed =>
            Uri.TryCreate(listed, UriKind.Absolute, out var uri) && IsLoopback(uri) &&
            string.Equals(uri.Host, asked.Host, StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath == asked.AbsolutePath);
    }

    /// <summary>Why an app can't be sent back to an address, or null when it can: https, or http on this computer.</summary>
    public static string? RedirectProblem(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return $"'{uri}' isn't an absolute address.";
        if (!string.IsNullOrEmpty(parsed.Fragment)) return $"'{uri}' has a fragment.";
        if (parsed.Scheme == Uri.UriSchemeHttps) return null;
        return parsed.Scheme == Uri.UriSchemeHttp && IsLoopback(parsed)
            ? null
            : $"'{uri}' must be https, or http to this computer (127.0.0.1 or localhost).";
    }

    /// <summary>Whether every address the app can be sent back to is on the person's own computer.</summary>
    public static bool OnlyLoopback(OAuthClient client) =>
        client.RedirectUris.All(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && IsLoopback(uri));

    static bool IsLoopback(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    OAuthClient? Registered(string clientId)
    {
        var body = clientId[RegisteredPrefix.Length..];
        var dot = body.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            var payload = Encoding.UTF8.GetString(SignInCodes.FromBase64Url(body[..dot]));
            var signature = SignInCodes.FromBase64Url(body[(dot + 1)..]);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload))))
                return null;
            var registration = JsonSerializer.Deserialize<Registration>(payload);
            return registration is null ? null : new OAuthClient(clientId, registration.Name, registration.RedirectUris);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    async Task<OAuthClient?> Fetch(string clientId)
    {
        // The spec's shape: https, with a path.
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var url) || url.AbsolutePath is "" or "/") return null;
        try
        {
            var http = HandlerOverride is { } handler ? new HttpClient(handler()) { Timeout = FetchTimeout } : Http.Value;
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDocumentBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > MaxDocumentBytes) return null;

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            // The document has to name itself by the address it was fetched from, or anyone's could be passed off as anyone's.
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("client_id", out var id) || id.GetString() != clientId ||
                !root.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array)
                return null;

            var redirectUris = uris.EnumerateArray().Select(u => u.GetString()).OfType<string>()
                .Where(u => RedirectProblem(u) is null).Take(MaxRedirectUris).ToList();
            if (redirectUris.Count == 0) return null;
            var name = root.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? Clean(n.GetString()) : null;
            return new OAuthClient(clientId, name ?? url.Host, redirectUris);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogInformation("Couldn't read the client metadata at {ClientId}: {Reason}", clientId, e.Message);
            return null;
        }
    }

    /// <summary>An app's name as people will see it: one line, not too long.</summary>
    static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var line = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return line.Length == 0 ? null : line.Length > 60 ? line[..60] : line;
    }

    static string CacheKey(string clientId) => "oauth-client:" + clientId;

    record Registration(string Name, List<string> RedirectUris);
}
