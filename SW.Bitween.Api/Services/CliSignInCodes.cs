using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SW.Bitween;

/// <summary>
/// The one-time codes that sign the bitween CLI in through a browser. A member signed in to the
/// admin UI, however they signed in, asks for a code for the CLI's challenge; the CLI trades it,
/// with the verifier only it holds, for a session of its own (PKCE, as OAuth's loopback flow does).
/// </summary>
/// <remarks>
/// Signed rather than stored, so any node can redeem a code another issued and nothing needs a
/// table: the key is derived from Token:Key, which every node shares, and is not the key access
/// tokens are signed with, so a code can never pass as one. A code is good for two minutes and only
/// with the verifier, which never leaves the CLI; one seen in a browser's history or a proxy's log
/// is no use to whoever saw it.
/// </remarks>
public class CliSignInCodes(IConfiguration configuration)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    const string Version = "c1";

    byte[] key;

    byte[] Key => key ??= SHA256.HashData(Encoding.UTF8.GetBytes("bitween-cli-sign-in:" +
        (configuration["Token:Key"] ?? throw new InvalidOperationException("Token:Key is not set."))));

    /// <summary>A code for this account and the CLI's S256 challenge.</summary>
    public string Issue(int accountId, string challenge, DateTimeOffset now)
    {
        if (!IsChallenge(challenge)) throw new ArgumentException("Not an S256 code challenge.", nameof(challenge));
        var payload = $"{Version}.{accountId}.{(now + Lifetime).ToUnixTimeSeconds()}.{challenge}." +
                      Base64Url(RandomNumberGenerator.GetBytes(12));
        return Base64Url(Encoding.UTF8.GetBytes(payload)) + "." + Base64Url(Sign(payload));
    }

    /// <summary>The account a code was issued for, when it is genuine, unexpired and the verifier matches.</summary>
    public int? Redeem(string code, string verifier, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > 128) return null;
        var dot = code.IndexOf('.');
        if (dot <= 0) return null;

        string payload;
        byte[] signature;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(code[..dot]));
            signature = FromBase64Url(code[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload))) return null;

        var parts = payload.Split('.');
        if (parts.Length != 5 || parts[0] != Version) return null;
        if (!int.TryParse(parts[1], out var accountId) || !long.TryParse(parts[2], out var expires)) return null;
        if (now.ToUnixTimeSeconds() > expires) return null;

        var expected = Encoding.ASCII.GetBytes(parts[3]);
        var actual = Encoding.ASCII.GetBytes(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? accountId : null;
    }

    /// <summary>An S256 challenge: the unpadded base64url of a SHA-256, 43 characters.</summary>
    public static bool IsChallenge(string challenge) =>
        challenge is { Length: 43 } && challenge.AsSpan().IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;

    byte[] Sign(string payload) => HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload));

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }
}
