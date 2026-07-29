using System.Text;
using Rebex.Net;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.RebexFtpUploadHandler;

public class NativeRebexFtpUploadHandler : INativeInfolinkHandler, IRequiresRebexLicense
{
    private readonly string? _licenseKey;
    private RebexFtpUploadHandlerInput _options = new();

    public NativeRebexFtpUploadHandler(string? licenseKey = null)
    {
        _licenseKey = licenseKey;
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        Rebex.Licensing.Key = _licenseKey;
        FtpProtocol.EnsurePasswordProvided(_options.Protocol, _options.Password);
        FtpProtocol.EnsurePrivateKeyProvided(_options.Protocol, _options.PrivateKey);

        IFtp ftpOrSftp;
        switch (_options.Protocol.ToLower())
        {
            case "sftpssh":
                var sftpssh = new Sftp();
                await sftpssh.ConnectAsync(_options.Host, _options.Port ?? 22);

                var keyBytes = Encoding.UTF8.GetBytes(SshKeyNormalizer.Normalize(_options.PrivateKey));
                var sshPrivateKey = new SshPrivateKey(keyBytes, _options.Password);
                await sftpssh.LoginAsync(_options.Username, sshPrivateKey);

                ftpOrSftp = sftpssh;
                break;

            case "sftp":
                var sftp = new Sftp();
                await sftp.ConnectAsync(_options.Host, _options.Port ?? 22);
                ftpOrSftp = sftp;
                await ftpOrSftp.LoginAsync(_options.Username, _options.Password);
                break;

            case "ftp":
                var ftp = new Rebex.Net.Ftp();
                await ftp.ConnectAsync(_options.Host, _options.Port ?? 21);
                ftpOrSftp = ftp;
                await ftpOrSftp.LoginAsync(_options.Username, _options.Password);
                break;

            default:
                throw new ArgumentException($"Unknown protocol '{_options.Protocol}'");
        }

        var bytes = _options.DataEncoding.ToLower() switch
        {
            "base64" => Convert.FromBase64String(xchangeFile.Data),
            "utf8" => Encoding.UTF8.GetBytes(xchangeFile.Data),
            _ => throw new ArgumentException(
                $"Unknown {nameof(RebexFtpUploadHandlerInput.DataEncoding)} '{_options.DataEncoding}'")
        };

        await using var stream = new MemoryStream(bytes);

        var filename = xchangeFile.Filename;
        if (string.IsNullOrWhiteSpace(filename))
        {
            var currentDate = DateTime.UtcNow;
            filename =
                $"{currentDate.Year:0000}{currentDate.Month:00}{currentDate.Day:00}{currentDate.Hour:00}{currentDate.Minute:00}{currentDate.Second:00}{currentDate.Millisecond:000}";
        }

        if (!string.IsNullOrWhiteSpace(_options.FileNamePrefix))
            filename = $"{_options.FileNamePrefix}_{filename}";

        await ftpOrSftp.PutFileAsync(stream, $"{_options.TargetPath}/{filename}");

        await ftpOrSftp.DisconnectAsync();
        return new XchangeFile(string.Empty);
    }

    public string Name => "NativeRebexFtpUploadHandler";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<RebexFtpUploadHandlerInput>();
    }

    public Type StartupValuesType => typeof(RebexFtpUploadHandlerInput);
}
