using System.Text;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.FtpReceiver;

/// <summary>
/// Picks up files from an FTP or SFTP server, with open-source clients (SSH.NET for SFTP, FluentFTP
/// for FTP) and so with no Rebex license. Behaves as <c>NativeRebexFtpReceiver</c> does, with the
/// same settings.
/// </summary>
public class NativeFtpReceiver : INativeInfolinkReceiver, IDisposable
{
    private FtpReceiverInput _options = new();
    private IFileTransfer? _transfer;

    private IFileTransfer Transfer => _transfer
        ?? throw new SWException("The FTP receiver was used before Initialize() ran.");

    public async Task Initialize()
    {
        _transfer = await FileTransfer.ConnectAsync(_options.Protocol, _options.Host, _options.Port,
            _options.Username, _options.Password, _options.PrivateKey, _options.HostKeyFingerprint);

        if (!string.IsNullOrEmpty(_options.TargetPath))
            await _transfer.ChangeDirectoryAsync(_options.TargetPath);
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
        var settledBefore = DateTime.UtcNow.AddSeconds(-_options.MinimumFileAgeSeconds);

        return files
            .Where(f => _options.MinimumFileAgeSeconds <= 0 || f.LastWriteTimeUtc is not { } written || written <= settledBefore)
            .Take(_options.BatchSize)
            .Select(f => f.Name)
            .ToList();
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        await using var stream = new MemoryStream();
        await Transfer.DownloadAsync(fileId, stream);

        var data = stream.ToArray();

        return _options.ResponseEncoding.ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(data), fileId),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(data), fileId),
            _ => throw new ArgumentException(
                $"Unknown {nameof(FtpReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        if (_options.CheckFileExistence && !await Transfer.ExistsAsync(fileId))
            return;

        if (string.IsNullOrWhiteSpace(_options.DeleteMovesFileTo))
            await Transfer.DeleteAsync(fileId);
        else
            await Transfer.RenameAsync(fileId, _options.DeleteMovesFileTo + "/" + fileId);
    }

    public string Name => "NativeFtpReceiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<FtpReceiverInput>();
    }

    public Type StartupValuesType => typeof(FtpReceiverInput);
}
