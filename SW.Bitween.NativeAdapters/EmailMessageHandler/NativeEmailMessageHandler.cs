using MimeKit;
using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.Bitween.NativeAdapters.SmtpHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.EmailMessageHandler;

/// <summary>
/// The Email channel type: emails a notification, laid out by Bitween, through the mail server set
/// in Settings. The channel only names the recipients.
/// </summary>
/// <remarks>
/// <see cref="NativeSmtpHandler"/> stays for anyone who wants their own subject and body templates,
/// or a server of their own per channel; both send through the same connection code.
/// </remarks>
/// <param name="accent">The instance's brand colour, for the email's top bar and button.</param>
public class NativeEmailMessageHandler(MailServerOptions mailServer, DestinationCircuits? circuits = null,
    string? accent = null)
    : INativeInfolinkHandler, IChannelMessageAdapter
{
    readonly DestinationCircuits circuits = circuits ?? DestinationCircuits.None;

    private EmailMessageInput _options = new();

    public string Name => nameof(NativeEmailMessageHandler);

    public Type StartupValuesType => typeof(EmailMessageInput);

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<EmailMessageInput>();
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var message = ChannelMessage.Parse(xchangeFile.Data);

        if (!mailServer.IsSet)
            throw new InvalidOperationException(
                "No mail server is set up, so Email channels cannot send. Add one in Settings, under Mail server.");

        var (subject, html, text) = EmailBody.Render(message, accent);

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(mailServer.FromName ?? string.Empty, mailServer.From));
        email.Subject = subject;
        var body = new BodyBuilder { HtmlBody = html, TextBody = text };
        // The logo travels inside the email, so it shows without fetching anything.
        var logo = body.LinkedResources.Add("bitween-logo.png", EmailBody.Logo(), new ContentType("image", "png"));
        logo.ContentId = EmailBody.LogoContentId;
        email.Body = body.ToMessageBody();

        NativeSmtpHandler.AddAddresses(email.To, _options.To);
        NativeSmtpHandler.AddAddresses(email.Cc, _options.Cc);
        NativeSmtpHandler.AddAddresses(email.Bcc, _options.Bcc);

        if (email.To.Count == 0 && email.Cc.Count == 0 && email.Bcc.Count == 0)
            throw new InvalidOperationException("This Email channel has nobody to send to.");

        await NativeSmtpHandler.Deliver(circuits, email, mailServer.Host!, mailServer.Port, mailServer.UseTls,
            mailServer.Username, mailServer.Password, mailServer.From!, mailServer.CertificateThumbprint);

        return new XchangeFile(subject, xchangeFile.Filename);
    }
}
