using System.Text;
using SW.Bitween.NativeAdapters.JsonMapper;
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

    /// <summary>Closes a connection a run left open - one that failed before Finalize.</summary>
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
public abstract class FileUpload<TSettings>(DestinationCircuits? circuits) : INativeInfolinkHandler
    where TSettings : IUploadSettings, new()
{
    readonly DestinationCircuits circuits = circuits ?? DestinationCircuits.None;

    protected TSettings Options { get; private set; } = new();

    private protected abstract Task<IFileTransfer> ConnectAsync();

    /// <summary>The server this uploads to, as its circuit is known: protocol, host and port.</summary>
    private protected abstract string Destination { get; }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        // Read before connecting, so a payload that can't be decoded never opens a connection.
        var bytes = Options.DataEncoding.ToLower() switch
        {
            "base64" => Convert.FromBase64String(xchangeFile.Data),
            "utf8" => Encoding.UTF8.GetBytes(xchangeFile.Data),
            _ => throw new ArgumentException($"Unknown DataEncoding '{Options.DataEncoding}'")
        };

        var filename = NameFor(xchangeFile, Options, DateTime.UtcNow);

        using var pass = circuits.Enter(Destination);
        try
        {
            await using var transfer = await ConnectAsync();
            await using var stream = new MemoryStream(bytes);
            // The path the Rebex adapter writes to, an empty TargetPath included.
            await transfer.UploadAsync(stream, $"{Options.TargetPath}/{filename}");
        }
        catch (Exception e)
        {
            pass.Failed(e);
            throw;
        }
        pass.Succeeded();

        return new XchangeFile(string.Empty);
    }

    /// <summary>
    /// What the uploaded file is called: the FileName setting when there is one, filled from a JSON
    /// message's fields (order-{{ orderId }}.json), else the message's own name, else the time; then
    /// the prefix. Without a name of its own, every message through one gateway carries the same name
    /// ({urlName}.json), and each upload replaced the one before it.
    /// </summary>
    internal static string NameFor(XchangeFile file, IUploadSettings options, DateTime now)
    {
        string? name = null;
        if (!string.IsNullOrWhiteSpace(options.FileName))
        {
            // Base64 data isn't a message with fields; its template is used as written.
            var data = options.DataEncoding.Equals("utf8", StringComparison.OrdinalIgnoreCase) ? file.Data : null;
            name = FtpProtocol.SafeFileName(ScribanJsonHelper.FillIfJson(options.FileName, data).Trim());
        }
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(file.Filename))
            name = FtpProtocol.SafeFileName(file.Filename);
        if (string.IsNullOrWhiteSpace(name))
            name = $"{now.Year:0000}{now.Month:00}{now.Day:00}{now.Hour:00}{now.Minute:00}{now.Second:00}{now.Millisecond:000}";

        return string.IsNullOrWhiteSpace(options.FileNamePrefix) ? name : $"{options.FileNamePrefix}_{name}";
    }

    public abstract string Name { get; }

    public void InitializeStartupValues(IDictionary<string, string> settings) =>
        Options = settings.ConvertTo<TSettings>();

    public Type StartupValuesType => typeof(TSettings);
}

internal static class SftpTimeouts
{
    /// <summary>The setting, or 60 seconds when it is missing or not above 0: never without a limit.</summary>
    public static TimeSpan Of(int seconds) => TimeSpan.FromSeconds(seconds > 0 ? seconds : 60);
}

/// <summary>Picks up files from an SFTP server, over SSH.NET. No license needed.</summary>
public class NativeSftpReceiver : FilePickup<SftpPickupSettings>
{
    private protected override Task<IFileTransfer> ConnectAsync() =>
        SftpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password,
            Options.PrivateKey, Options.HostKeyFingerprint, SftpTimeouts.Of(Options.TimeoutSeconds));

    public override string Name => "NativeSftpReceiver";
}

/// <summary>Uploads the message as a file to an SFTP server, over SSH.NET. No license needed.</summary>
public class NativeSftpUploadHandler(DestinationCircuits? circuits = null) : FileUpload<SftpUploadSettings>(circuits)
{
    private protected override string Destination => $"sftp://{Options.Host}:{Options.Port ?? 22}";

    private protected override Task<IFileTransfer> ConnectAsync() =>
        SftpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password,
            Options.PrivateKey, Options.HostKeyFingerprint, SftpTimeouts.Of(Options.TimeoutSeconds));

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
public class NativeFtpUploadHandler(DestinationCircuits? circuits = null) : FileUpload<FtpUploadSettings>(circuits)
{
    private protected override string Destination =>
        $"ftp://{Options.Host}:{Options.Port ?? ((Options.Encryption ?? "").Trim().Equals("implicit", StringComparison.OrdinalIgnoreCase) ? 990 : 21)}";

    private protected override Task<IFileTransfer> ConnectAsync() =>
        FtpTransfer.ConnectAsync(Options.Host, Options.Port, Options.Username, Options.Password!,
            Options.Encryption, Options.CertificateThumbprint, Options.PassiveMode);

    public override string Name => "NativeFtpUploadHandler";
}
