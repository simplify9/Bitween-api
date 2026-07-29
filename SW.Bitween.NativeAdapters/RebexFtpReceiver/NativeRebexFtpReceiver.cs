using System.Text;
using Rebex.Net;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.RebexFtpReceiver;

public class NativeRebexFtpReceiver : INativeInfolinkReceiver, IRequiresRebexLicense
{
    private readonly string? _licenseKey;
    private RebexFtpReceiverInput _options = new();
    private IFtp _ftpOrSftp = null!;

    public NativeRebexFtpReceiver(string? licenseKey = null)
    {
        _licenseKey = licenseKey;
    }

    public async Task Initialize()
    {
        Rebex.Licensing.Key = _licenseKey;
        FtpProtocol.EnsurePasswordProvided(_options.Protocol, _options.Password);
        FtpProtocol.EnsurePrivateKeyProvided(_options.Protocol, _options.PrivateKey);

        switch (_options.Protocol.ToLower())
        {
            case "sftpssh":
                var sftpssh = new Sftp();
                await sftpssh.ConnectAsync(_options.Host, _options.Port ?? 22);

                var keyBytes = Encoding.UTF8.GetBytes(SshKeyNormalizer.Normalize(_options.PrivateKey));
                var privateKey = new SshPrivateKey(keyBytes, _options.Password);
                await sftpssh.LoginAsync(_options.Username, privateKey);

                _ftpOrSftp = sftpssh;
                break;

            case "sftp":
                var sftp = new Sftp();
                await sftp.ConnectAsync(_options.Host, _options.Port ?? 22);
                _ftpOrSftp = sftp;
                await _ftpOrSftp.LoginAsync(_options.Username, _options.Password);
                break;

            case "ftp":
                var ftp = new Rebex.Net.Ftp();
                await ftp.ConnectAsync(_options.Host, _options.Port ?? 21);
                _ftpOrSftp = ftp;
                await _ftpOrSftp.LoginAsync(_options.Username, _options.Password);
                break;

            default:
                throw new ArgumentException($"Unknown protocol '{_options.Protocol}'");
        }

        if (!string.IsNullOrEmpty(_options.TargetPath))
            await _ftpOrSftp.ChangeDirectoryAsync(_options.TargetPath);
    }

    public async Task Finalize()
    {
        await _ftpOrSftp.DisconnectAsync();
        _ftpOrSftp.Dispose();
    }

    public async Task<IEnumerable<string>> ListFiles()
    {
        var files = await _ftpOrSftp.GetListAsync();

        return files
            .Where(i => i.IsFile)
            .Take(_options.BatchSize)
            .Select(i => i.Name)
            .ToList();
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        await using var stream = new MemoryStream();
        await _ftpOrSftp.GetFileAsync(fileId, stream);

        var data = stream.ToArray();

        return _options.ResponseEncoding.ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(data), fileId),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(data), fileId),
            _ => throw new ArgumentException(
                $"Unknown {nameof(RebexFtpReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        if (_options.CheckFileExistence && !await _ftpOrSftp.FileExistsAsync(fileId))
            return;

        if (string.IsNullOrWhiteSpace(_options.DeleteMovesFileTo))
            await _ftpOrSftp.DeleteFileAsync(fileId);
        else
            await _ftpOrSftp.RenameAsync(fileId, _options.DeleteMovesFileTo + "/" + fileId);
    }

    public string Name => "NativeRebexFtpReceiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<RebexFtpReceiverInput>();
    }

    public Type StartupValuesType => typeof(RebexFtpReceiverInput);
}
