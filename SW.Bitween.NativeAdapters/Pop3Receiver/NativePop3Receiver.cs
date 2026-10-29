using System.Text;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.Pop3Receiver;

public class NativePop3Receiver : INativeInfolinkReceiver, IDisposable
{
    private Pop3ReceiverInput _options = new();
    private Pop3Client? _client;

    // For tests pointing at a local fake server: when set, these win over the settings.
    internal int? Port { get; set; }
    internal bool? UseSsl { get; set; }

    /// <summary>
    /// Implicit TLS on 995 unless the settings say otherwise, as it always was. With TLS, the server's
    /// certificate must be valid or, when a thumbprint is pinned, be exactly that certificate: how a
    /// mail server with its own certificate is trusted. Either way it's checked before the password goes.
    /// </summary>
    public async Task Initialize()
    {
        var security = UseSsl switch
        {
            true => SecureSocketOptions.SslOnConnect,
            false => SecureSocketOptions.None,
            null => (_options.Encryption ?? "").Trim().ToLowerInvariant() switch
            {
                "" or "implicit" => SecureSocketOptions.SslOnConnect,
                "explicit" => SecureSocketOptions.StartTls,
                // No unencrypted option: the receiver always signs in with a password.
                _ => throw new ArgumentException($"Unknown Encryption '{_options.Encryption}': use implicit or explicit."),
            },
        };
        var port = Port ?? _options.Port ?? (security == SecureSocketOptions.SslOnConnect ? 995 : 110);

        _client = new Pop3Client();
        string? refused = null;
        if (!string.IsNullOrWhiteSpace(_options.CertificateThumbprint))
        {
            var pinned = FtpTransfer.NormalizeThumbprint(_options.CertificateThumbprint);
            _client.ServerCertificateValidationCallback = (_, certificate, _, _) =>
            {
                var presented = certificate == null ? "" : FtpTransfer.Thumbprint(certificate);
                if (presented == pinned) return true;
                refused = presented;
                return false;
            };
        }

        try
        {
            await _client.ConnectAsync(_options.Host, port, security);
        }
        catch (Exception) when (refused is not null)
        {
            throw new InvalidOperationException(
                $"The POP3 server's certificate (SHA-256 {refused}) is not the one configured. Refusing to send credentials to it.");
        }

        await _client.AuthenticateAsync(_options.Username, _options.Password);
    }

    public async Task Finalize()
    {
        await _client!.DisconnectAsync(true);
        _client.Dispose();
        _client = null;
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
        // A part can carry no content at all - a malformed or truncated message. Treating that
        // as an empty attachment beats a NullReferenceException from inside the receive loop.
        if (attachment is MessagePart { Message: { } embedded })
            await embedded.WriteToAsync(memoryStream);
        else if (attachment is MimePart { Content: { } body })
            await body.DecodeToAsync(memoryStream);

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

    /// <summary>
    /// Closes a connection a run left open - one that failed before Finalize. Without the QUIT that
    /// Finalize sends, the server keeps the messages this run marked for deletion.
    /// </summary>
    public void Dispose()
    {
        try { _client?.Dispose(); } catch { /* closing only */ }
        _client = null;
    }
}
