using System;
using System.Security.Cryptography;
using System.Text;

namespace SW.Bitween.Services;

/// <summary>
/// Encrypts secret settings before they're stored, so a database dump or backup never carries a
/// license key in the clear. AES-GCM with a fresh salt and nonce per value: encrypting the same
/// key twice produces different ciphertext, and tampering fails the authentication tag rather
/// than decrypting to garbage.
/// <para>
/// The passphrase comes from <see cref="BitweenOptions.SettingsEncryptionKey"/> — configuration
/// only, never the Settings table. When it isn't set, <see cref="IsConfigured"/> is false and
/// secret settings stay out of the table entirely.
/// </para>
/// </summary>
public class SettingsProtector
{
    private const string Prefix = "enc.v1:";
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 100_000;

    private readonly string _passphrase;

    public SettingsProtector(BitweenOptions options) => _passphrase = options.SettingsEncryptionKey;

    /// <summary>Whether secrets can be stored at all. False = no passphrase configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_passphrase);

    public string Protect(string plaintext)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"{BitweenOptions.ConfigurationSection}:{nameof(BitweenOptions.SettingsEncryptionKey)} is not configured.");

        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[Encoding.UTF8.GetByteCount(plaintext)];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(DeriveKey(salt), TagBytes);
        aes.Encrypt(nonce, Encoding.UTF8.GetBytes(plaintext), cipher, tag);

        var payload = new byte[SaltBytes + NonceBytes + TagBytes + cipher.Length];
        salt.CopyTo(payload, 0);
        nonce.CopyTo(payload, SaltBytes);
        tag.CopyTo(payload, SaltBytes + NonceBytes);
        cipher.CopyTo(payload, SaltBytes + NonceBytes + TagBytes);

        return Prefix + Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Reverses <see cref="Protect"/>. A value without the marker is returned untouched — that's
    /// how a hand-written row, or one stored before a passphrase existed, still reads back.
    /// Throws when the value was written under a different passphrase or has been altered.
    /// </summary>
    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        var payload = Convert.FromBase64String(stored[Prefix.Length..]);
        if (payload.Length < SaltBytes + NonceBytes + TagBytes)
            throw new CryptographicException("Encrypted setting value is truncated.");

        var salt = payload.AsSpan(0, SaltBytes);
        var nonce = payload.AsSpan(SaltBytes, NonceBytes);
        var tag = payload.AsSpan(SaltBytes + NonceBytes, TagBytes);
        var cipher = payload.AsSpan(SaltBytes + NonceBytes + TagBytes);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(DeriveKey(salt.ToArray()), TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] DeriveKey(byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(_passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
}
