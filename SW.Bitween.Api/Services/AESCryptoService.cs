using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SW.Bitween;

public static class AESCryptoService
{
    /// <summary>
    /// The salt this has always used. It is a constant, and a well known one - the bytes spell
    /// "Ivan Medvedev" from an old MSDN sample - so it adds nothing an attacker does not have.
    /// It cannot be changed without making every value already encrypted undecryptable, so it
    /// stays until there is a migration to move ciphertext to a per-value salt.
    /// </summary>
    private static readonly byte[] LegacySalt =
        [0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76];

    public static string Decrypt(string encryptedText, string password)
    {
        
        if (string.IsNullOrEmpty(encryptedText))
            return "";

        var encryptedBytes = Convert.FromBase64String(encryptedText);

        byte[] decryptedBytes = null;
        using (var ms = new MemoryStream())
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;

                // PBKDF2 emits one continuous stream, so deriving key and IV in a single call and
                // splitting it is byte-for-byte what two successive GetBytes calls produced. SHA1
                // and 1000 iterations are the obsolete constructor's defaults, kept so already
                // encrypted values still decrypt.
                var keyLength = aes.KeySize / 8;
                var material = Rfc2898DeriveBytes.Pbkdf2(
                    password, LegacySalt, 1000, HashAlgorithmName.SHA1, keyLength + aes.BlockSize / 8);

                aes.Key = material[..keyLength];
                aes.IV = material[keyLength..];

                aes.Mode = CipherMode.CBC;

                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(encryptedBytes, 0, encryptedBytes.Length);
                    cs.Close();
                }

                decryptedBytes = ms.ToArray();
            }
        }

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    public static string Encrypt(string text, string password)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        byte[] encryptedBytes;
        var plainBytes = Encoding.UTF8.GetBytes(text);

        using (var ms = new MemoryStream())
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;

                // PBKDF2 emits one continuous stream, so deriving key and IV in a single call and
                // splitting it is byte-for-byte what two successive GetBytes calls produced. SHA1
                // and 1000 iterations are the obsolete constructor's defaults, kept so already
                // encrypted values still decrypt.
                var keyLength = aes.KeySize / 8;
                var material = Rfc2898DeriveBytes.Pbkdf2(
                    password, LegacySalt, 1000, HashAlgorithmName.SHA1, keyLength + aes.BlockSize / 8);

                aes.Key = material[..keyLength];
                aes.IV = material[keyLength..];

                aes.Mode = CipherMode.CBC;

                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.Close();
                }

                encryptedBytes = ms.ToArray();
            }
        }

        return Convert.ToBase64String(encryptedBytes);
    }
}