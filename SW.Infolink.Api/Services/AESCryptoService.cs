using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SW.Infolink;

public static class AESCryptoService
{
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

                var key = new Rfc2898DeriveBytes(password,
                    new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);

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

                var key = new Rfc2898DeriveBytes(password,
                    new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);

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