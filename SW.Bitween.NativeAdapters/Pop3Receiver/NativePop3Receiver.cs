using System.Text;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.Pop3Receiver;

public class NativePop3Receiver : INativeInfolinkReceiver
{
    private Pop3ReceiverInput _options = new();
    private Pop3Client? _client;

    // Connection defaults matching the standard implicit-SSL POP3 endpoint.
    // Not user-configurable; exposed internally only so tests can point at a local fake server.
    internal int Port { get; set; } = 995;
    internal bool UseSsl { get; set; } = true;

    public async Task Initialize()
    {
        _client = new Pop3Client();
        var sslOptions = UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
        await _client.ConnectAsync(_options.Host, Port, sslOptions);
        await _client.AuthenticateAsync(_options.Username, _options.Password);
    }

    public async Task Finalize()
    {
        await _client!.DisconnectAsync(true);
        _client.Dispose();
    }

    public Task<IEnumerable<string>> ListFiles()
    {
        var count = Math.Min(_client!.Count, _options.BatchSize);
        return Task.FromResult(Enumerable.Range(0, count).Select(i => i.ToString()));
    }

    public async Task<XchangeFile> GetFile(string fileId)
    {
        var index = int.Parse(fileId);
        var message = await _client!.GetMessageAsync(index);

        var attachment = message.Attachments.FirstOrDefault();
        if (attachment == null)
            return new XchangeFile(message.TextBody ?? message.HtmlBody ?? string.Empty, message.Subject);

        using var memoryStream = new MemoryStream();
        if (attachment is MessagePart rfc822)
            await rfc822.Message.WriteToAsync(memoryStream);
        else
            await ((MimePart)attachment).Content.DecodeToAsync(memoryStream);

        var buffer = memoryStream.ToArray();

        return _options.ResponseEncoding.ToLower() switch
        {
            "base64" => new XchangeFile(Convert.ToBase64String(buffer), message.Subject),
            "utf8" => new XchangeFile(Encoding.UTF8.GetString(buffer), message.Subject),
            _ => throw new ArgumentException(
                $"Unknown {nameof(Pop3ReceiverInput.ResponseEncoding)} '{_options.ResponseEncoding}'")
        };
    }

    public async Task DeleteFile(string fileId)
    {
        await _client!.DeleteMessageAsync(int.Parse(fileId));
    }

    public string Name => "NativePop3Receiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<Pop3ReceiverInput>();
    }

    public Type StartupValuesType => typeof(Pop3ReceiverInput);
}
