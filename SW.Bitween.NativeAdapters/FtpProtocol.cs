namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Shared FTP protocol rules used by the native FTP adapters, so the handler and receiver
/// can never disagree on them.
/// </summary>
public static class FtpProtocol
{
    /// <summary>
    /// Password-authenticated protocols (ftp, sftp) require a password. sftpssh authenticates
    /// with a private key, so its "password" is an optional key passphrase and is not enforced.
    /// </summary>
    public static void EnsurePasswordProvided(string protocol, string? password)
    {
        if (protocol.ToLower() is "ftp" or "sftp" && string.IsNullOrEmpty(password))
            throw new ArgumentException($"Password is required for the '{protocol}' protocol.");
    }

    /// <summary>
    /// sftpssh authenticates with a private key, so one must be provided. ftp/sftp authenticate
    /// with a password instead, so no key is required for them.
    /// </summary>
    public static void EnsurePrivateKeyProvided(string protocol, string? privateKey)
    {
        if (protocol.ToLower() is "sftpssh" && string.IsNullOrWhiteSpace(privateKey))
            throw new ArgumentException($"A private key is required for the '{protocol}' protocol.");
    }
}
