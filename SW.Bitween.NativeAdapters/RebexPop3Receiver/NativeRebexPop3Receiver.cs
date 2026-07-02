using System.Text;
using Rebex.Net;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.RebexPop3Receiver;

public class NativeRebexPop3Receiver : INativeInfolinkReceiver
{
    public const string LicenseKeyEnvironmentVariable = "REBEX_LICENSE_KEY";

    private RebexPop3ReceiverInput _options = new();
    private Pop3 _pop3 = new();

    // Connection defaults matching the standard implicit-SSL POP3 endpoint.
    // Not user-configurable; exposed internally only so tests can point at a local fake server.
    internal int Port { get; set; } = 995;
    internal bool UseSsl { get; set; } = true;

    public async Task Initialize()
    {
        Rebex.Licensing.Key = Environment.GetEnvironmentVariable(LicenseKeyEnvironmentVariable);
        _pop3 = new Pop3();
        var sslMode = UseSsl ? SslMode.Implicit : SslMode.None;
        await _pop3.ConnectAsync(_options.Host, Port, sslMode);
        await _pop3.LoginAsync(_options.Username, _options.Password);
    }

    public async Task Finalize()
    {
        await _pop3.DisconnectAsync(false);
        _pop3.Dispose();
    }

    public async Task<IEnumerable<string>> ListFiles()
    {
        var messages = await _pop3.GetMessageListAsync(Pop3ListFields.Fast);

        return messages.Select(m => m.SequenceNumber.ToString())
            .Take(_options.BatchSize)
            .ToList();
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        var sequenceNumber = int.Parse(fileId);
        var message = await _pop3.GetMailMessageAsync(sequenceNumber);

        if (message.Attachments.Count < 1)
            return new XchangeFile(message.BodyText, message.Subject);

        var attachment = message.Attachments[0];
        await _pop3.GetMessageAsync(sequenceNumber, attachment.FileName);

        await using var stream = attachment.GetContentStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var buffer = memoryStream.ToArray();

        return _options.ResponseEncoding.ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(buffer), message.Subject),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(buffer), message.Subject),
            _ => throw new ArgumentException(
                $"Unknown {nameof(RebexPop3ReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        await _pop3.DeleteAsync(int.Parse(fileId));
    }

    public string Name => "NativeRebexPop3Receiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<RebexPop3ReceiverInput>();
    }

    public Type StartupValuesType => typeof(RebexPop3ReceiverInput);
}
