using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// Checks the signature a signed gateway call carries against one secret. Pure: no partner, no
/// database, so every scheme can be tested on its own.
/// </summary>
public static class GatewaySignatures
{
    public const int DefaultToleranceSeconds = 300;

    /// <summary>Whether <paramref name="header"/> is a signature of <paramref name="body"/> made with <paramref name="secret"/>.</summary>
    public static bool Verify(GatewaySignatureScheme scheme, string header, ReadOnlySpan<byte> body, string secret,
        int? toleranceSeconds, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(secret)) return false;
        var key = Encoding.UTF8.GetBytes(secret);
        return scheme switch
        {
            GatewaySignatureScheme.HmacSha256 => VerifyPlain(header.Trim(), body, key),
            GatewaySignatureScheme.TimestampedHmacSha256 => VerifyTimestamped(header, body, key,
                toleranceSeconds ?? DefaultToleranceSeconds, now),
            _ => false,
        };
    }

    static bool VerifyPlain(string header, ReadOnlySpan<byte> body, byte[] key)
    {
        if (header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) header = header[7..];
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, body, mac);
        return Matches(header, mac);
    }

    /// <remarks>
    /// <c>t=…,v1=…</c>, in any order, possibly with several <c>v1</c> while the sender rotates its
    /// secret: any one matching is enough. Other parts (a <c>v0</c>) are ignored.
    /// </remarks>
    static bool VerifyTimestamped(string header, ReadOnlySpan<byte> body, byte[] key, int tolerance, DateTimeOffset now)
    {
        string timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name == "t") timestamp = value;
            else if (name == "v1") signatures.Add(value);
        }

        if (timestamp == null || signatures.Count == 0) return false;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        // Some senders write milliseconds; a value that large can only be that.
        if (seconds > 100_000_000_000) seconds /= 1000;
        if (Math.Abs(now.ToUnixTimeSeconds() - seconds) > tolerance) return false;

        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, signed, mac);

        var matched = false;
        foreach (var signature in signatures)
            matched |= Matches(signature, mac);
        return matched;
    }

    /// <summary>Hex (either case) or base64, compared in constant time.</summary>
    static bool Matches(string sent, ReadOnlySpan<byte> mac)
    {
        byte[] bytes = null;
        if (sent.Length == mac.Length * 2)
        {
            try { bytes = Convert.FromHexString(sent); }
            catch (FormatException) { }
        }
        if (bytes == null)
        {
            try { bytes = Convert.FromBase64String(sent); }
            catch (FormatException) { return false; }
        }
        return bytes.Length == mac.Length && CryptographicOperations.FixedTimeEquals(bytes, mac);
    }
}
