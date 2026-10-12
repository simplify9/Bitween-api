extern alias BouncyCastle2;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using BouncyCastle2::Org.BouncyCastle.Crypto.Signers;
using BouncyCastle2::Org.BouncyCastle.Security;
using BouncyCastle2::Org.BouncyCastle.X509;

namespace SW.Bitween.Services.Marketplace;

/// <summary>
/// The registry's signatures, checked here exactly as the registry makes them: Ed25519 over plain
/// lines of text. See the Bitween-Registry README for the formats; the two must never drift.
/// </summary>
public static class RegistrySignatures
{
    public static string PublishMessage(string packageId, string version, string sha256) =>
        $"bitween-registry/v1/publish\n{packageId}\n{version}\n{sha256.ToLowerInvariant()}";

    public static string CountersignMessage(string packageId, string version, string sha256, string publisher,
        string publisherKeyId, string tier, DateTime signedOn) =>
        $"bitween-registry/v1/countersign\n{packageId}\n{version}\n{sha256.ToLowerInvariant()}\n" +
        $"{publisher}\n{publisherKeyId}\n{tier.ToLowerInvariant()}\n{Timestamp(signedOn)}";

    public static string RevocationsMessage(string payload) => $"bitween-registry/v1/revocations\n{payload}";

    /// <summary>A time the registry sent is UTC even when it arrives without a zone; only a local time is converted.</summary>
    public static string Timestamp(DateTime value) =>
        (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc))
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>The trusted keys from the setting: base64 or PEM, separated by commas or new lines.</summary>
    /// <returns>The keys that could be read, and the entries that couldn't.</returns>
    public static (IReadOnlyList<Ed25519PublicKeyParameters> Keys, IReadOnlyList<string> Unreadable) TrustedKeys(string setting)
    {
        var keys = new List<Ed25519PublicKeyParameters>();
        var unreadable = new List<string>();
        foreach (var entry in SplitKeys(setting))
        {
            try
            {
                keys.Add(ReadPublic(entry));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or SecurityUtilityException or System.IO.IOException)
            {
                unreadable.Add(entry.Length > 16 ? entry[..16] + "..." : entry);
            }
        }

        return (keys, unreadable);
    }

    public static Ed25519PublicKeyParameters ReadPublic(string text)
    {
        var body = string.Concat(text.Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("-----", StringComparison.Ordinal)));
        return PublicKeyFactory.CreateKey(Convert.FromBase64String(body)) as Ed25519PublicKeyParameters
               ?? throw new FormatException("Not an Ed25519 public key.");
    }

    /// <summary>The first 16 hex characters of the SHA-256 of the key's DER, as the registry names publisher keys.</summary>
    public static string KeyIdOf(Ed25519PublicKeyParameters key) =>
        Convert.ToHexStringLower(SHA256.HashData(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key).GetDerEncoded()))[..16];

    public static bool Verify(Ed25519PublicKeyParameters key, string message, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, key);
        var bytes = Encoding.UTF8.GetBytes(message);
        verifier.BlockUpdate(bytes, 0, bytes.Length);
        return verifier.VerifySignature(signatureBytes);
    }

    public static bool VerifyAny(IEnumerable<Ed25519PublicKeyParameters> keys, string message, string signature) =>
        keys.Any(k => Verify(k, message, signature));

    /// <summary>
    /// Splits the setting into keys. A PEM key spans lines, so lines are joined back between its
    /// BEGIN and END markers; anything else is one key per line or per comma.
    /// </summary>
    static IEnumerable<string> SplitKeys(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) yield break;
        var pem = new StringBuilder();
        foreach (var raw in setting.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("-----BEGIN", StringComparison.Ordinal)) { pem.Clear(); pem.Append(line).Append('\n'); continue; }
            if (pem.Length > 0)
            {
                pem.Append(line).Append('\n');
                if (line.StartsWith("-----END", StringComparison.Ordinal)) { yield return pem.ToString(); pem.Clear(); }
                continue;
            }

            foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part;
        }
    }
}
