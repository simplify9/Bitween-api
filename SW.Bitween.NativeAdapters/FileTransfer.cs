using System.Text;
using FluentFTP;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SW.Bitween.NativeAdapters;

/// <summary>A file on the server, as a receiver lists it.</summary>
internal record RemoteFile(string Name, DateTime? LastWriteTimeUtc);

/// <summary>
/// One connection to an FTP or SFTP server, signed in and in its target directory: the few things
/// the FTP adapters do with one. SSH.NET carries SFTP and FluentFTP carries FTP, both open source,
/// so these adapters need no Rebex license. Paths that aren't absolute are relative to the
/// directory changed into, as they were with Rebex.
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

internal static class FileTransfer
{
    /// <summary>
    /// Connects and signs in with the protocol the settings name — <c>sftp</c> or <c>ftp</c> with a
    /// password, <c>sftpssh</c> with a private key whose passphrase, if any, is the password — under
    /// the same rules as the Rebex adapters.
    /// </summary>
    public static async Task<IFileTransfer> ConnectAsync(string protocol, string host, int? port, string username,
        string? password, string? privateKey, string? hostKeyFingerprint)
    {
        FtpProtocol.EnsurePasswordProvided(protocol, password);
        FtpProtocol.EnsurePrivateKeyProvided(protocol, privateKey);

        switch (protocol.ToLower())
        {
            case "sftpssh":
            {
                using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(SshKeyNormalizer.Normalize(privateKey)));
                var key = string.IsNullOrEmpty(password)
                    ? new PrivateKeyFile(keyStream)
                    : new PrivateKeyFile(keyStream, password);
                return await SftpTransfer.ConnectAsync(new ConnectionInfo(host, port ?? 22, username,
                    new PrivateKeyAuthenticationMethod(username, key)), hostKeyFingerprint);
            }
            case "sftp":
                return await SftpTransfer.ConnectAsync(new ConnectionInfo(host, port ?? 22, username,
                    new PasswordAuthenticationMethod(username, password)), hostKeyFingerprint);
            case "ftp":
                return await FtpTransfer.ConnectAsync(host, port ?? 21, username, password!);
            default:
                throw new ArgumentException($"Unknown protocol '{protocol}'");
        }
    }

    private sealed class SftpTransfer(SftpClient client) : IFileTransfer
    {
        public static async Task<IFileTransfer> ConnectAsync(ConnectionInfo connection, string? expectedFingerprint)
        {
            var client = new SftpClient(connection);
            string? refusedKey = null;
            // Checked during the key exchange, before authentication: a server presenting any other
            // key is turned away before a password or key signature is sent to it.
            client.HostKeyReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(expectedFingerprint)) return;
                e.CanTrust = FtpProtocol.NormalizeFingerprint(e.FingerPrintSHA256) ==
                             FtpProtocol.NormalizeFingerprint(expectedFingerprint);
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

    private sealed class FtpTransfer(AsyncFtpClient client) : IFileTransfer
    {
        public static async Task<IFileTransfer> ConnectAsync(string host, int port, string username, string password)
        {
            // Plain FTP in passive mode, as the Rebex adapter connects; listing times in UTC so a
            // file's age is measured the same way as over SFTP.
            var client = new AsyncFtpClient(host, username, password, port, new FtpConfig
            {
                TimeConversion = FtpDate.UTC,
            });
            try
            {
                await client.Connect();
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }

            return new FtpTransfer(client);
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
            var status = await client.UploadStream(from, path, FtpRemoteExists.Overwrite);
            if (status == FtpStatus.Failed)
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
}
