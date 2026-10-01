using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SW.Bitween;

/// <summary>
/// Links to exchange files that Bitween serves itself, for readers with no Bitween login: an
/// aggregation's handler working through a roll-up, a partner following the response link of its
/// exchange, an API client reading the exchange list.
/// </summary>
/// <remarks>
/// <para>
/// Exchange files are private in storage, so a storage URL no longer opens. A link here carries a seal
/// made from the file's storage key with a server secret: it opens that one file, and changing any
/// character of it — to reach another exchange — breaks the seal. Nothing is stored per link.
/// </para>
/// <para>
/// A link lives as long as its file, like the public storage URL it replaces. The secret is derived
/// from <c>Token:Key</c>, so there's nothing new to configure, and rotating that key (which also signs
/// everyone out) invalidates every link handed out before.
/// </para>
/// <para>
/// The path ends in the storage key itself (<c>.../temp30/docs/{id}/output</c>), the same tail the
/// storage URL had, so a reader that takes the exchange id or file type out of the link still finds it.
/// </para>
/// </remarks>
public class FileLinks(BitweenOptions options, IConfiguration configuration, IHttpContextAccessor httpContextAccessor,
    IServer server = null)
{
    public const string RoutePrefix = "api/files";

    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? string.Empty), 32,
        info: Encoding.UTF8.GetBytes("bitween:file-links:v1"));

    /// <summary>A TCP address Kestrel listens on; not a Unix socket or pipe (<c>http://unix:/tmp/…</c>), which resident adapters use.</summary>
    private static readonly Regex ListenAddress =
        new(@"^(?<scheme>https?)://(?!unix:|pipe:)(?<host>\[[^\]]*\]|[^:/]+)(?<port>:\d+)?/?$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The address readers reach Bitween on: the Public address setting, or else the address the
    /// current request came in on, or else — a scheduled job — <see cref="InstanceUrl"/>.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options.PublicUrl)) return options.PublicUrl.TrimEnd('/');

            var request = httpContextAccessor.HttpContext?.Request;
            return request is { Host.HasValue: true }
                ? $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/')
                : InstanceUrl;
        }
    }

    /// <summary>
    /// The address this instance listens on, as the adapters it runs reach it: they run beside it, in
    /// the same container, so a wildcard binding (<c>http://+:8080</c>) is <c>localhost</c>, and plain
    /// HTTP is preferred — no certificate for them to trust. Nothing outside the instance can open a
    /// link on it. <c>null</c> with no server.
    /// </summary>
    public string InstanceUrl
    {
        get
        {
            var listening = (server?.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
                .Select(a => ListenAddress.Match(a)).Where(m => m.Success).ToList();
            var match = listening.FirstOrDefault(m => m.Groups["scheme"].Value.Equals("http", StringComparison.OrdinalIgnoreCase))
                        ?? listening.FirstOrDefault();
            if (match is null) return null;

            var host = match.Groups["host"].Value;
            if (host is "+" or "*" or "[::]" or "0.0.0.0") host = "localhost";
            return $"{match.Groups["scheme"].Value.ToLowerInvariant()}://{host}{match.Groups["port"].Value}";
        }
    }

    /// <summary>
    /// The address an adapter reaches Bitween on: the Public address setting, or else <see cref="InstanceUrl"/> —
    /// never a request's, which is wherever the person who clicked happened to be.
    /// </summary>
    public string AdapterBaseUrl =>
        !string.IsNullOrWhiteSpace(options.PublicUrl) ? options.PublicUrl.TrimEnd('/') : InstanceUrl;

    /// <summary>
    /// A link to the file at <paramref name="storageKey"/>, or <c>null</c> when there's no address to build it on.
    /// <paramref name="forAdapter"/> builds it on <see cref="AdapterBaseUrl"/>.
    /// </summary>
    public string LinkTo(string storageKey, bool forAdapter = false) =>
        (forAdapter ? AdapterBaseUrl : BaseUrl) is { } baseUrl
            ? $"{baseUrl}/{RoutePrefix}/{Seal(storageKey)}/{storageKey}"
            : null;

    /// <summary>Whether <paramref name="seal"/> was made by this server for exactly <paramref name="storageKey"/>.</summary>
    public bool Opens(string seal, string storageKey) =>
        !string.IsNullOrEmpty(seal) && !string.IsNullOrEmpty(storageKey) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Seal(storageKey)), Encoding.ASCII.GetBytes(seal));

    /// <summary>128 bits of HMAC-SHA256 over the key, base64url: short enough for a path segment, far too long to guess.</summary>
    private string Seal(string storageKey)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(storageKey));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
