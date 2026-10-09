using System.Text;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.FtpUploadHandler;

/// <summary>
/// Uploads the message as a file to an FTP or SFTP server, with open-source clients (SSH.NET for
/// SFTP, FluentFTP for FTP) and so with no Rebex license. Behaves as
/// <c>NativeRebexFtpUploadHandler</c> does, with the same settings.
/// </summary>
public class NativeFtpUploadHandler : INativeInfolinkHandler
{
    private FtpUploadHandlerInput _options = new();

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        // Read before connecting, so a payload that can't be decoded never opens a connection.
        var bytes = _options.DataEncoding.ToLower() switch
        {
            "base64" => Convert.FromBase64String(xchangeFile.Data),
            "utf8" => Encoding.UTF8.GetBytes(xchangeFile.Data),
            _ => throw new ArgumentException(
                $"Unknown {nameof(FtpUploadHandlerInput.DataEncoding)} '{_options.DataEncoding}'")
        };

        var filename = string.IsNullOrWhiteSpace(xchangeFile.Filename)
            ? null
            : FtpProtocol.SafeFileName(xchangeFile.Filename);
        if (string.IsNullOrWhiteSpace(filename))
        {
            var currentDate = DateTime.UtcNow;
            filename =
                $"{currentDate.Year:0000}{currentDate.Month:00}{currentDate.Day:00}{currentDate.Hour:00}{currentDate.Minute:00}{currentDate.Second:00}{currentDate.Millisecond:000}";
        }

        if (!string.IsNullOrWhiteSpace(_options.FileNamePrefix))
            filename = $"{_options.FileNamePrefix}_{filename}";

        await using var transfer = await FileTransfer.ConnectAsync(_options.Protocol, _options.Host, _options.Port,
            _options.Username, _options.Password, _options.PrivateKey, _options.HostKeyFingerprint);
        await using var stream = new MemoryStream(bytes);
        // The same path the Rebex adapter writes to, an empty TargetPath included.
        await transfer.UploadAsync(stream, $"{_options.TargetPath}/{filename}");

        return new XchangeFile(string.Empty);
    }

    public string Name => "NativeFtpUploadHandler";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<FtpUploadHandlerInput>();
    }

    public Type StartupValuesType => typeof(FtpUploadHandlerInput);
}
