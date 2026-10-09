using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentFTP;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SW.Bitween.NativeAdapters;

/// <summary>A file on the server, as a receiver lists it.</summary>
internal record RemoteFile(string Name, DateTime? LastWriteTimeUtc);

/// <summary>
/// One connection to a file server, signed in: the few things the FTP and SFTP adapters do with
/// one. Paths that aren't absolute are relative to the directory changed into.
/// </summary>
internal interface IFileTransfer : IAsyncDisposable
{
    Task ChangeDirectoryAsync(string path);
    Task<IReadOnlyList<RemoteFile>> ListFilesAsync();
    Task DownloadAsync(string path, Stream into);
    Task UploadAsync(Stream from, string path);
    Task<bool> ExistsAsync(string path);
    Task DeleteAsync(string path);
    Task RenameAsync(string from, string to);
}

/// <summary>SFTP, over SSH.NET.</summary>
internal sealed class SftpTransfer(SftpClient client) : IFileTransfer
{
    /// <summary>
    /// Signs in with the private key when one is given — its passphrase, if any, is the password —
    /// and with the password otherwise. A pinned host key is checked during the key exchange,
    /// before authentication: a server presenting any other key is turned away before a password
    /// or key signature is sent to it.
    /// </summary>
    public static async Task<IFileTransfer> ConnectAsync(string host, int? port, string username,
        string? password, string? privateKey, string? hostKeyFingerprint)
    {
        AuthenticationMethod authentication;
        if (!string.IsNullOrWhiteSpace(privateKey))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(SshKeyNormalizer.Normalize(privateKey)));
            var key = string.IsNullOrEmpty(password)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, password);
            authentication = new PrivateKeyAuthenticationMethod(username, key);
        }
        else if (!string.IsNullOrEmpty(password))
            authentication = new PasswordAuthenticationMethod(username, password);
        else
            throw new ArgumentException("SFTP needs a password or a private key to sign in with.");

        var client = new SftpClient(new ConnectionInfo(host, port ?? 22, username, authentication));
        string? refusedKey = null;
        client.HostKeyReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(hostKeyFingerprint)) return;
            e.CanTrust = FtpProtocol.NormalizeFingerprint(e.FingerPrintSHA256) ==
                         FtpProtocol.NormalizeFingerprint(hostKeyFingerprint);
            if (!e.CanTrust) refusedKey = FtpProtocol.NormalizeFingerprint(e.FingerPrintSHA256);
        };

        try
        {
            await client.ConnectAsync(CancellationToken.None);
        }
        catch (SshConnectionException) when (refusedKey is not null)
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"The SFTP server's host key (SHA256:{refusedKey}) is not the one configured. " +
                "Refusing to send credentials to it.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new SftpTransfer(client);
    }

    public Task ChangeDirectoryAsync(string path) => client.ChangeDirectoryAsync(path);

    public async Task<IReadOnlyList<RemoteFile>> ListFilesAsync()
    {
        var files = new List<RemoteFile>();
        await foreach (var file in client.ListDirectoryAsync(".", CancellationToken.None))
            if (file.IsRegularFile)
                files.Add(new RemoteFile(file.Name, file.LastWriteTimeUtc));
        return files;
    }

    public Task DownloadAsync(string path, Stream into) => client.DownloadFileAsync(path, into);

    public Task UploadAsync(Stream from, string path) => client.UploadFileAsync(from, path);

    public Task<bool> ExistsAsync(string path) => client.ExistsAsync(path);

    public Task DeleteAsync(string path) => client.DeleteFileAsync(path, CancellationToken.None);

    public Task RenameAsync(string from, string to) => client.RenameFileAsync(from, to, CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (client.IsConnected) client.Disconnect();
        }
        finally
        {
            client.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>FTP and FTPS, over FluentFTP.</summary>
internal sealed class FtpTransfer(AsyncFtpClient client) : IFileTransfer
{
    /// <summary>
    /// Connects in the given encryption mode — <c>none</c>, <c>explicit</c> (AUTH TLS, port 21) or
    /// <c>implicit</c> (TLS from the first byte, port 990). With TLS, the server's certificate must
    /// be valid, or, when a thumbprint is pinned, be exactly that certificate — which is how a
    /// partner's self-signed certificate is trusted. Either way the check is made during the TLS
    /// handshake, before the credentials are sent.
    /// </summary>
    public static async Task<IFileTransfer> ConnectAsync(string host, int? port, string username, string password,
        string encryption, string? certificateThumbprint, bool passive)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("FTP needs a password to sign in with.");

        var mode = (encryption ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "none" => FtpEncryptionMode.None,
            "explicit" => FtpEncryptionMode.Explicit,
            "implicit" => FtpEncryptionMode.Implicit,
            _ => throw new ArgumentException($"Unknown Encryption '{encryption}': use none, explicit or implicit."),
        };

        var client = new AsyncFtpClient(host, username, password,
            port ?? (mode == FtpEncryptionMode.Implicit ? 990 : 21),
            new FtpConfig
            {
                EncryptionMode = mode,
                DataConnectionType = passive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive,
                // Listing times in UTC, so a file's age is measured as over SFTP.
                TimeConversion = FtpDate.UTC,
            });

        string? refusedCertificate = null;
        client.ValidateCertificate += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(certificateThumbprint))
            {
                e.Accept = e.PolicyErrors == System.Net.Security.SslPolicyErrors.None;
                return;
            }

            var presented = Thumbprint(e.Certificate);
            e.Accept = presented == NormalizeThumbprint(certificateThumbprint);
            if (!e.Accept) refusedCertificate = presented;
        };

        try
        {
            await client.Connect();
        }
        catch (Exception) when (refusedCertificate is not null)
        {
            await client.DisposeAsync();
            throw new InvalidOperationException(
                $"The FTPS server's certificate (SHA-256 {refusedCertificate}) is not the one configured. " +
                "Refusing to send credentials to it.");
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        return new FtpTransfer(client);
    }

    /// <summary>The certificate's SHA-256 thumbprint, as upper-case hex.</summary>
    internal static string Thumbprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    /// <summary>"AB:CD ef", "abcdef" and "SHA256:ABCDEF" are the same thumbprint.</summary>
    internal static string NormalizeThumbprint(string thumbprint)
    {
        var value = thumbprint.Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value[7..];
        return new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    public Task ChangeDirectoryAsync(string path) => client.SetWorkingDirectory(path);

    public async Task<IReadOnlyList<RemoteFile>> ListFilesAsync() =>
        (await client.GetListing())
        .Where(i => i.Type == FtpObjectType.File)
        .Select(i => new RemoteFile(i.Name, i.Modified == DateTime.MinValue ? null : i.Modified))
        .ToList();

    public async Task DownloadAsync(string path, Stream into)
    {
        if (!await client.DownloadStream(into, path))
            throw new IOException($"'{path}' could not be downloaded.");
    }

    public async Task UploadAsync(Stream from, string path)
    {
        if (await client.UploadStream(from, path, FtpRemoteExists.Overwrite) == FtpStatus.Failed)
            throw new IOException($"'{path}' could not be uploaded.");
    }

    public Task<bool> ExistsAsync(string path) => client.FileExists(path);

    public Task DeleteAsync(string path) => client.DeleteFile(path);

    public Task RenameAsync(string from, string to) => client.Rename(from, to);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client.IsConnected) await client.Disconnect();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
