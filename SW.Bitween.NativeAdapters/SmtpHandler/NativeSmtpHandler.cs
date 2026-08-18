using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using Newtonsoft.Json.Linq;
using SW.Bitween.NativeAdapters.JsonMapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.SmtpHandler;

/// <summary>
/// Sends the payload as an email, with the subject and body written as templates over it.
/// </summary>
/// <remarks>
/// Built for cases where the recipient is a person rather than a system — a retry budget running
/// out, a notifier on a failed exchange — which is why the subject and body are templated instead
/// of the payload being emailed raw. A JSON blob in an inbox tells nobody anything.
/// </remarks>
public class NativeSmtpHandler : INativeInfolinkHandler
{
    private SmtpHandlerInput _options = new();

    public string Name => "NativeSmtpHandler";

    public Type StartupValuesType => typeof(SmtpHandlerInput);

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<SmtpHandlerInput>();
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var subject = Fill(_options.Subject, xchangeFile.Data);
        var body = Fill(_options.Body, xchangeFile.Data);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName ?? string.Empty, _options.From));
        message.Subject = subject;
        message.Body = new TextPart(_options.IsHtml ? TextFormat.Html : TextFormat.Plain) { Text = body };

        AddAddresses(message.To, _options.To);
        AddAddresses(message.Cc, _options.Cc);
        AddAddresses(message.Bcc, _options.Bcc);

        if (message.To.Count == 0 && message.Cc.Count == 0 && message.Bcc.Count == 0)
            throw new InvalidOperationException("No recipients were configured for the SMTP handler.");

        using var client = new SmtpClient();

        // Auto picks STARTTLS or implicit SSL from the port, which is what makes one adapter work
        // against 587 and 465 without asking the client which handshake their provider uses.
        await client.ConnectAsync(_options.Host, _options.Port,
            _options.UseTls ? SecureSocketOptions.Auto : SecureSocketOptions.None);

        // A relay that accepts unauthenticated mail from inside the network is a normal setup, so
        // only authenticate when a password was actually supplied.
        if (!string.IsNullOrWhiteSpace(_options.Password))
            await client.AuthenticateAsync(
                string.IsNullOrWhiteSpace(_options.Username) ? _options.From : _options.Username,
                _options.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        return new XchangeFile(subject, xchangeFile.Filename);
    }

    private static void AddAddresses(InternetAddressList list, string? addresses)
    {
        if (string.IsNullOrWhiteSpace(addresses)) return;

        foreach (var address in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                     | StringSplitOptions.TrimEntries))
            list.Add(MailboxAddress.Parse(address));
    }

    /// <summary>
    /// Renders a template against the payload, or returns it unchanged when the payload is not JSON.
    /// </summary>
    /// <remarks>
    /// Non-JSON payloads are normal for a pipeline handler — a CSV or a flat file on its way out —
    /// and those have no fields to substitute. A broken template still throws, so a typo in a
    /// placeholder is not quietly emailed as literal text.
    /// </remarks>
    internal static string Fill(string template, string payload)
    {
        if (string.IsNullOrEmpty(template) || !LooksLikeJson(payload)) return template;

        return ScribanJsonHelper.RenderText(template, payload);
    }

    private static bool LooksLikeJson(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        var trimmed = payload.TrimStart();
        if (trimmed[0] is not ('{' or '[')) return false;

        try
        {
            JToken.Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
