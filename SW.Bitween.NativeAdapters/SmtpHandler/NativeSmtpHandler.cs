using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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

        // Revocation stays switched on, so a certificate the CA has actually revoked is still refused.
        // What the callback softens is the other outcome: the lookup needs the CA's OCSP or CRL server
        // to be reachable, which the corporate networks Bitween runs inside routinely block, and
        // MailKit's default treats "could not find out" exactly like "revoked". That rejected a
        // perfectly good Gmail certificate — one OpenSSL accepts on the same machine — and the alert
        // never went out. Soft-failing an undeterminable status is what browsers and mail clients do;
        // every other defect, revocation included, still fails.
        client.CheckCertificateRevocation = true;
        client.ServerCertificateValidationCallback = (_, _, chain, errors) =>
            IsCertificateAcceptable(errors,
                chain?.ChainStatus.Select(s => s.Status) ?? Enumerable.Empty<X509ChainStatusFlags>());

        // Named rather than left to Auto: on any port but 465, Auto means "encrypt if the server
        // offers it", so a server that does not offer STARTTLS — or an offer stripped in transit —
        // silently continues in the clear. StartTls demands it and fails if it is not there. 465 is
        // the implicit-TLS port, where the handshake happens before any of that is negotiable.
        var security = _options.UseTls
            ? _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(_options.Host, _options.Port, security);

        // A relay that accepts unauthenticated mail from inside the network is a normal setup, so
        // only authenticate when a password was actually supplied.
        if (!string.IsNullOrWhiteSpace(_options.Password))
        {
            // Refusing beats sending the credential over a connection anyone on the path can read.
            if (!client.IsSecure)
                throw new InvalidOperationException(
                    "The SMTP handler will not send a password over an unencrypted connection. " +
                    "Set UseTls to true, or clear the password if the relay does not need one.");

            await client.AuthenticateAsync(
                string.IsNullOrWhiteSpace(_options.Username) ? _options.From : _options.Username,
                _options.Password);
        }

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

    /// <summary>
    /// Whether a server certificate should be accepted, given what validation found wrong with it.
    /// </summary>
    /// <remarks>
    /// Only one defect is tolerated: a revocation status that could not be established, because the
    /// CA's OCSP or CRL server was unreachable. A certificate the CA has revoked, an untrusted root,
    /// a wrong hostname and an expired certificate are all still refused — as is any chain flag not
    /// named here, so a defect nobody thought about fails closed rather than slipping through.
    /// </remarks>
    internal static bool IsCertificateAcceptable(SslPolicyErrors errors,
        IEnumerable<X509ChainStatusFlags> chainStatus)
    {
        if (errors == SslPolicyErrors.None) return true;

        // A missing certificate or the wrong name on one is not a revocation question at all, and the
        // chain flags say nothing about either.
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors) return false;

        const X509ChainStatusFlags undeterminable =
            X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

        // Masked rather than compared: one entry can carry several flags at once, and "revoked" set
        // alongside "could not check" has to fail.
        return chainStatus.All(status => (status & ~undeterminable) == X509ChainStatusFlags.NoError);
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
