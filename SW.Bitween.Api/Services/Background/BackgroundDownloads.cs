using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SW.Bitween.Services.Background;

/// <summary>
/// Links to a background request's file - an export's zip - that a browser can download the ordinary
/// way, straight to disk, rather than holding it in memory first: a browser following a link can't send
/// the sign-in header.
/// </summary>
/// <remarks>
/// A link carries its expiry and a seal over the request's id and that expiry, made with a secret
/// derived from <c>Token:Key</c>, as <see cref="FileLinks"/> are: any node can check it, and changing a
/// character of it breaks it. Only the request's owner is handed one, and it opens that one file for
/// <see cref="Lasts"/>.
/// </remarks>
public class BackgroundDownloads(IConfiguration configuration)
{
    /// <summary>How long a link opens its file: long enough to start the download it was asked for.</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(10);

    private readonly byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? string.Empty), 32,
        info: Encoding.UTF8.GetBytes("bitween:background-downloads:v1"));

    /// <summary>The address, under <c>/api</c>, that downloads <paramref name="id"/>'s file until it expires.</summary>
    public string LinkTo(string id)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lasts).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"/api/background/{id}/file?expires={expires}&seal={Seal(id, expires)}";
    }

    /// <summary>Whether a link's expiry and seal open <paramref name="id"/>'s file now.</summary>
    public bool Opens(string id, string expires, string seal)
    {
        if (string.IsNullOrEmpty(seal) || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var at) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() > at)
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Seal(id, expires)), Encoding.ASCII.GetBytes(seal));
    }

    private string Seal(string id, string expires) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}|{expires}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
