using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace SW.Bitween;

/// <summary>
/// Encrypts the columns that hold credentials - partner and global values, data source settings,
/// the adapter properties of subscriptions, notifiers and alerts, and the copies of them each
/// exchange keeps - so a copy of the database, or a backup, is not a copy of every password.
/// </summary>
/// <remarks>
/// <para>
/// The whole column is encrypted, not the values marked secret. Adapters published as packages
/// rarely declare which of their properties are secret, so encrypting only declared ones would
/// leave exactly those exposed.
/// </para>
/// <para>
/// Keyed by <c>Bitween:SettingsEncryptionKey</c>, the passphrase the settings secrets already use.
/// Without it nothing is encrypted and nothing changes. A stored value carries the <c>enc:v1:</c>
/// prefix, so plaintext written before this - or by a deployment with no key - still reads. To
/// change the key, move the old one to <c>Bitween:PreviousSettingsEncryptionKey</c>: values are read
/// with either and written with the new one.
/// </para>
/// <para>
/// A key that is gone for good leaves nothing to read them with. <c>Bitween:SettingsEncryptionKeyLost</c>
/// then reads such a value as empty, so it can be entered again, and leaves what is stored in place
/// until it is: should the key turn up after all, whatever wasn't entered again reads once more.
/// </para>
/// </remarks>
public static class SecretColumnCipher
{
    public const string Prefix = "enc:v1:";

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    // Derived once, not per value: these columns are read on every exchange, and the per-value
    // PBKDF2 the settings use would cost a hundred thousand rounds each time.
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("bitween/secret-columns/v1");

    private static byte[] _key;
    private static byte[] _previousKey;
    private static bool _keyLost;

    /// <summary>Whether values are being encrypted as they are written.</summary>
    public static bool Enabled => _key is not null;

    /// <summary>Whether a value no key here can read is taken as empty rather than refused.</summary>
    public static bool KeyLost => _keyLost;

    public static void Configure(string passphrase, string previousPassphrase = null, bool keyLost = false)
    {
        _key = string.IsNullOrWhiteSpace(passphrase) ? null : Derive(passphrase);
        _previousKey = string.IsNullOrWhiteSpace(previousPassphrase) ? null : Derive(previousPassphrase);
        _keyLost = keyLost;
    }

    public static string Protect(string plaintext)
    {
        if (_key is null || plaintext is null || plaintext.StartsWith(Prefix, StringComparison.Ordinal))
            return plaintext;

        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceBytes + data.Length + TagBytes];
        var nonce = output.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, data, output.AsSpan(NonceBytes, data.Length), output.AsSpan(NonceBytes + data.Length));
        return Prefix + Convert.ToBase64String(output);
    }

    public static string Unprotect(string stored) => Unprotect(stored, unreadable: null);

    /// <summary>The same for JSON, where a value that can't be read is JSON's own empty value.</summary>
    private static string UnprotectJson(string stored) => Unprotect(stored, unreadable: "null");

    private static string Unprotect(string stored, string unreadable)
    {
        if (stored is null || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        var input = Convert.FromBase64String(stored[Prefix.Length..]);
        foreach (var key in new[] { _key, _previousKey })
        {
            if (key is null) continue;
            try
            {
                return Decrypt(key, input);
            }
            catch (AuthenticationTagMismatchException)
            {
                // Not this key; try the previous one.
            }
        }

        if (_keyLost) return unreadable;
        throw new UnreadableSecretException();
    }

    private static string Decrypt(byte[] key, byte[] input)
    {
        var length = input.Length - NonceBytes - TagBytes;
        var plaintext = new byte[length];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(input.AsSpan(0, NonceBytes), input.AsSpan(NonceBytes, length),
            input.AsSpan(NonceBytes + length), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] Derive(string passphrase) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, Salt, 210_000, HashAlgorithmName.SHA256, 32);

    /// <summary>
    /// Wraps the converter already on the property - <c>StoreAsJson</c>, typically - so the JSON it
    /// produces is encrypted on the way to the database and decrypted on the way back.
    /// </summary>
    public static PropertyBuilder<T> Encrypted<T>(this PropertyBuilder<T> builder)
    {
        var inner = builder.Metadata.GetValueConverter()
                    ?? throw new InvalidOperationException(
                        $"{builder.Metadata.Name} has no converter to encrypt the output of; call StoreAsJson first.");
        var comparer = builder.Metadata.GetValueComparer();

        var cipher = new ValueConverter<string, string>(v => Protect(v), v => UnprotectJson(v));
        builder.Metadata.SetValueConverter(inner.ComposeWith(cipher));
        if (comparer is not null) builder.Metadata.SetValueComparer(comparer);
        return builder;
    }

    /// <summary>A text column holding a credential itself, encrypted as the JSON columns are.</summary>
    public static PropertyBuilder<string> EncryptedText(this PropertyBuilder<string> builder) =>
        builder.HasConversion(new ValueConverter<string, string>(v => Protect(v), v => Unprotect(v)));

    /// <summary>
    /// The same for a PostgreSQL <c>jsonb</c> column, which must always hold valid JSON. The
    /// encrypted payload is stored as a JSON string, so the column stays valid; a column written
    /// before encryption - a plain JSON object - still reads.
    /// </summary>
    public static PropertyBuilder<T> EncryptedJsonb<T>(this PropertyBuilder<T> builder) where T : class
    {
        builder.HasConversion(
            new ValueConverter<T, string>(v => ToJsonb(v), s => FromJsonb<T>(s)),
            new ValueComparer<T>(
                (a, b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b),
                v => v == null ? 0 : JsonConvert.SerializeObject(v).GetHashCode(),
                v => v == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(v))));
        builder.HasColumnType("jsonb");
        return builder;
    }

    private static string ToJsonb<T>(T value)
    {
        if (value is null) return null;
        var json = JsonConvert.SerializeObject(value);
        return Enabled ? JsonConvert.SerializeObject(Protect(json)) : json;
    }

    private static T FromJsonb<T>(string stored) where T : class
    {
        if (stored is null) return null;
        var json = stored.TrimStart().StartsWith("\"" + Prefix, StringComparison.Ordinal)
            ? UnprotectJson(JsonConvert.DeserializeObject<string>(stored))
            : stored;
        return JsonConvert.DeserializeObject<T>(json);
    }

    /// <summary>
    /// For a key-value column (PostgreSQL <c>hstore</c>) rather than JSON: each value is encrypted
    /// on its own, and the keys stay readable.
    /// </summary>
    public static PropertyBuilder<System.Collections.Generic.Dictionary<string, string>> EncryptedValues(
        this PropertyBuilder<System.Collections.Generic.Dictionary<string, string>> builder)
    {
        builder.HasConversion(
            new ValueConverter<System.Collections.Generic.Dictionary<string, string>, System.Collections.Generic.Dictionary<string, string>>(
                v => MapValues(v, true), v => MapValues(v, false)),
            new ValueComparer<System.Collections.Generic.Dictionary<string, string>>(
                (a, b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b),
                v => v == null ? 0 : JsonConvert.SerializeObject(v).GetHashCode(),
                v => v == null ? null : new System.Collections.Generic.Dictionary<string, string>(v)));
        return builder;
    }

    private static System.Collections.Generic.Dictionary<string, string> MapValues(
        System.Collections.Generic.Dictionary<string, string> values, bool protect)
    {
        if (values is null) return null;
        var result = new System.Collections.Generic.Dictionary<string, string>(values.Count);
        foreach (var (key, value) in values) result[key] = protect ? Protect(value) : Unprotect(value);
        return result;
    }
}

/// <summary>A stored credential that neither of this instance's keys can decrypt.</summary>
public class UnreadableSecretException() : InvalidOperationException(
    "A stored value is encrypted with a key this instance does not have. Set " +
    "Bitween:SettingsEncryptionKey to the key it was written with, or put that key in " +
    "Bitween:PreviousSettingsEncryptionKey while moving to a new one.");
