using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SW.Bitween.Services;

/// <summary>
/// A small value sealed for a round trip through the browser, as a cookie: encrypted and
/// authenticated with AES-GCM under a key derived from <c>Token:Key</c> for one purpose, so it can
/// be read only by Bitween, on any node, and only for that purpose.
/// </summary>
/// <remarks>
/// What an OAuth2 or OpenID Connect redirect has to remember between leaving for the provider and
/// coming back - the state, the nonce, the PKCE verifier - travels this way, so nothing is stored.
/// </remarks>
public class SealedState(IConfiguration configuration)
{
    byte[] Key(string purpose) => HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(configuration["Token:Key"] ?? string.Empty), 32,
        info: Encoding.UTF8.GetBytes(purpose));

    public string Seal<T>(string purpose, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key(purpose), 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return Base64UrlEncoder.Encode([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>The value, or the default when it was tampered with, sealed for another purpose, or isn't one at all.</summary>
    public T Open<T>(string purpose, string sealedValue)
    {
        if (string.IsNullOrEmpty(sealedValue)) return default;
        try
        {
            var bytes = Base64UrlEncoder.DecodeBytes(sealedValue);
            if (bytes.Length < 29) return default;
            var plain = new byte[bytes.Length - 28];
            using (var aes = new AesGcm(Key(purpose), 16)) aes.Decrypt(bytes[..12], bytes[28..], bytes[12..28], plain);
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return default;
        }
    }

    public static string Random(int bytes = 32) => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>The PKCE S256 challenge for a verifier.</summary>
    public static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
