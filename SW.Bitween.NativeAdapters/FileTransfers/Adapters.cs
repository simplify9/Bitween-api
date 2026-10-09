using System.Text;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.FileTransfers;

/// <summary>
/// Picking files up from a file server, the same whichever protocol reaches it: the SFTP and FTP
/// pickups differ only in how they connect. Behaves as the Rebex FTP receiver does.
/// </summary>
public abstract class FilePickup<TSettings> : INativeInfolinkReceiver, IDisposable
    where TSettings : IPickupSettings, new()
{
    protected TSettings Options { get; private set; } = new();
    private IFileTransfer? _transfer;

    private IFileTransfer Transfer => _transfer
        ?? throw new SWException($"The {Name} was used before Initialize() ran.");

    private protected abstract Task<IFileTransfer> ConnectAsync();

    public async Task Initialize()
    {
        _transfer = await ConnectAsync();
        if (!string.IsNullOrEmpty(Options.TargetPath))
            await _transfer.ChangeDirectoryAsync(Options.TargetPath);
    }

    public async Task Finalize()
    {
        if (_transfer is not null) await _transfer.DisposeAsync();
        _transfer = null;
    }

    /// <summary>Closes a connection a run left open — one that failed before Finalize.</summary>
    public void Dispose()
    {
        try { _transfer?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* closing only */ }
        _transfer = null;
    }

    public async Task<IEnumerable<string>> ListFiles()
    {
        var files = await Transfer.ListFilesAsync();

        // A file still being uploaded is listed like any other; taking it half-written sends half a
        // document on, then deletes the rest as it arrives. Left alone until it has been still for a while.
        var settledBefore = DateTime.UtcNow.AddSeconds(-Options.MinimumFileAgeSeconds);

        return files
            .Where(f => Options.MinimumFileAgeSeconds <= 0 || f.LastWriteTimeUtc is not { } written || written <= settledBefore)
            .Take(Options.BatchSize)
            .Select(f => f.Name)
            .ToList();
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        await using var stream = new MemoryStream();
        await Transfer.DownloadAsync(fileId, stream);
        var data = stream.ToArray();

        return Options.ResponseEncoding.ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(data), fileId),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(data), fileId),
            _ => throw new ArgumentException($"Unknown ResponseEncoding '{Options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        if (Options.CheckFileExistence && !await Transfer.ExistsAsync(fileId))
            return;

        if (string.IsNullOrWhiteSpace(Options.DeleteMovesFileTo))
            await Transfer.DeleteAsync(fileId);
        else
            await Transfer.RenameAsync(fileId, Options.DeleteMovesFileTo + "/" + fileId);
    }

    public abstract string Name { get; }

    public void InitializeStartupValues(IDictionary<string, string> settings) =>
        Options = settings.ConvertTo<TSettings>();

    public Type StartupValuesType => typeof(TSettings);
}

/// <summary>
/// Uploading the message as a file, the same whichever protocol reaches the server. Behaves as the
/// Rebex FTP upload handler does.
/// </summary>
public abstract class FileUpload<TSettings> : INativeInfolinkHandler
    where TSettings : IUploadSettings, new()
{
    protected TSettings Options { get; private set; } = new();

    private protected abstract Task<IFileTransfer> ConnectAsync();

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        // Read before connecting, so a payload that can't be decoded never opens a connection.
        var bytes = Options.DataEncoding.ToLower() switch
        {
            "base64" => Convert.FromBase64String(xchangeFile.Data),
            "utf8" => Encoding.UTF8.GetBytes(xchangeFile.Data),
            _ => throw new ArgumentException($"Unknown DataEncoding '{Options.DataEncoding}'")
        };

        var filename = string.IsNullOrWhiteSpace(xchangeFile.Filename)
            ? null
            : FtpProtocol.SafeFileName(xchangeFile.Filename);
        if (string.IsNullOrWhiteSpace(filename))
        {
            var now = DateTime.UtcNow;
            filename = $"{now.Year:0000}{now.Month:00}{now.Day:00}{now.Hour:00}{now.Minute:00}{now.Second:00}{now.Millisecond:000}";
        }

        if (!string.IsNullOrWhiteSpace(Options.FileNamePrefix))
            filename = $"{Options.FileNamePrefix}_{filename}";

        await using var transfer = await ConnectAsync();
        await using var stream = new MemoryStream(bytes);
        // The path the Rebex adapter writes to, an empty TargetPath included.
        await transfer.UploadAsync(stream, $"{Options.TargetPath}/{filename}");

        return new XchangeFile(string.Empty);
    }

    public abstract string Name { get; }

    public void InitializeStartupValues(IDictionary<string, string> settings) =>
        Options = settings.ConvertTo<TSettings>();

    public Type StartupValuesType => typeof(TSettings);
}

/// <summary>Picks up files from an SFTP server, over SSH.NET. No license needed.</summary>
public class NativeSftpReceiver : FilePickup<SftpPickupSettings>
{
    private protected override Task<IFileTransfer> ConnectAsync() =>
        SftpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password,
            Options.PrivateKey, Options.HostKeyFingerprint);

    public override string Name => "NativeSftpReceiver";
}

/// <summary>Uploads the message as a file to an SFTP server, over SSH.NET. No license needed.</summary>
public class NativeSftpUploadHandler : FileUpload<SftpUploadSettings>
{
    private protected override Task<IFileTransfer> ConnectAsync() =>
        SftpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password,
            Options.PrivateKey, Options.HostKeyFingerprint);

    public override string Name => "NativeSftpUploadHandler";
}

/// <summary>Picks up files from an FTP or FTPS server, over FluentFTP. No license needed.</summary>
public class NativeFtpReceiver : FilePickup<FtpPickupSettings>
{
    private protected override Task<IFileTransfer> ConnectAsync() =>
        FtpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password!,
            Options.Encryption, Options.CertificateThumbprint, Options.PassiveMode);

    public override string Name => "NativeFtpReceiver";
}

/// <summary>Uploads the message as a file to an FTP or FTPS server, over FluentFTP. No license needed.</summary>
public class NativeFtpUploadHandler : FileUpload<FtpUploadSettings>
{
    private protected override Task<IFileTransfer> ConnectAsync() =>
        FtpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password!,
            Options.Encryption, Options.CertificateThumbprint, Options.PassiveMode);

    public override string Name => "NativeFtpUploadHandler";
}
