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

    /// <summary>
    /// Refuses an SFTP server whose host key is not the one configured, before any credential is
    /// sent to it. Without a pinned key the client accepts whatever key the server presents, so
    /// anyone who can intercept the connection receives the password and the files. Left
    /// unconfigured, the connection is accepted as before; set it to the SHA-256 fingerprint that
    /// <c>ssh-keygen -lf</c> prints for the server's key.
    /// </summary>
    public static void VerifyHostKey(Rebex.Net.Sftp sftp, string? expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            return;

        var actual = sftp.ServerKey.Fingerprint.ToString(Rebex.Security.Certificates.SignatureHashAlgorithm.SHA256, true);
        if (NormalizeFingerprint(actual) != NormalizeFingerprint(expectedFingerprint))
        {
            sftp.Disconnect();
            throw new InvalidOperationException(
                $"The SFTP server's host key (SHA256:{NormalizeFingerprint(actual)}) is not the one configured. " +
                "Refusing to send credentials to it.");
        }
    }

    /// <summary>"SHA256:abc=" and "abc" are the same fingerprint, as OpenSSH and Rebex print it.</summary>
    internal static string NormalizeFingerprint(string fingerprint)
    {
        var value = fingerprint.Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value[7..];
        return value.TrimEnd('=');
    }

    /// <summary>
    /// One file name, with no directory in it. The name comes from the exchange — a POP3 receiver
    /// sets it from the email's subject — so "../../etc/x" would otherwise climb out of the
    /// configured target directory on the partner's server.
    /// </summary>
    internal static string SafeFileName(string filename)
    {
        var name = filename.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        return name is "" or "." or ".." ? null : name;
    }
}
