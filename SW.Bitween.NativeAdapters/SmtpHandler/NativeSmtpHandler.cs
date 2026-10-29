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
/// Built for cases where the recipient is a person rather than a system - a retry budget running
/// out, a notifier on a failed exchange - which is why the subject and body are templated instead
/// of the payload being emailed raw. A JSON blob in an inbox tells nobody anything.
/// </remarks>
public class NativeSmtpHandler(DestinationCircuits? circuits = null) : INativeInfolinkHandler
{
    readonly DestinationCircuits circuits = circuits ?? DestinationCircuits.None;

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

        await Deliver(circuits, message, _options.Host, _options.Port, _options.UseTls, _options.Username,
            _options.Password, _options.From, _options.CertificateThumbprint);

        return new XchangeFile(subject, xchangeFile.Filename);
    }

    /// <summary>
    /// Connects, signs in when there is a password, and sends. Shared with the Email channel's
    /// adapter, so both make the same choices about encryption, certificates and the circuit breaker.
    /// </summary>
    /// <param name="username">Empty signs in as <paramref name="from"/>.</param>
    /// <param name="connectLimit">How long reaching the server may take; <see cref="ConnectLimit"/> unless a test says otherwise.</param>
    internal static async Task Deliver(DestinationCircuits circuits, MimeMessage message, string host, int port,
        bool useTls, string? username, string? password, string from, string? certificateThumbprint,
        TimeSpan? connectLimit = null)
    {
        using var client = new SmtpClient();

        // Revocation stays switched on, so a certificate the CA has actually revoked is still refused.
        // What the callback softens is the other outcome: the lookup needs the CA's OCSP or CRL server
        // to be reachable, which the corporate networks Bitween runs inside routinely block, and
        // MailKit's default treats "could not find out" exactly like "revoked". That rejected a
        // perfectly good Gmail certificate - one OpenSSL accepts on the same machine - and the alert
        // never went out. Soft-failing an undeterminable status is what browsers and mail clients do;
        // every other defect, revocation included, still fails.
        client.CheckCertificateRevocation = true;
        // A pinned certificate is trusted for exactly what it is, a self-signed one included: how a
        // relay with its own certificate is used without trusting every certificate.
        var pinned = string.IsNullOrWhiteSpace(certificateThumbprint)
            ? null
            : FtpTransfer.NormalizeThumbprint(certificateThumbprint);
        client.ServerCertificateValidationCallback = (_, certificate, chain, errors) =>
            pinned != null
                ? certificate != null && FtpTransfer.Thumbprint(certificate) == pinned
                : IsCertificateAcceptable(errors,
                    chain?.ChainStatus.Select(s => s.Status) ?? Enumerable.Empty<X509ChainStatusFlags>());

        // Named rather than left to Auto: on any port but 465, Auto means "encrypt if the server
        // offers it", so a server that does not offer STARTTLS - or an offer stripped in transit -
        // silently continues in the clear. StartTls demands it and fails if it is not there. 465 is
        // the implicit-TLS port, where the handshake happens before any of that is negotiable.
        var security = useTls
            ? port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        // Admitted once the message is built: one that can't be built fails the same whether the relay is up or not.
        using var pass = circuits.Enter($"smtp://{host}:{port}");
        try
        {
            await Send(client, security, message, host, port, username, password, from, connectLimit ?? ConnectLimit);
        }
        catch (Exception e)
        {
            pass.Failed(e);
            throw;
        }
        pass.Succeeded();
    }

    /// <summary>
    /// How long connecting may take: reaching the server, its greeting and the TLS handshake. Left to
    /// MailKit it is two minutes, which is what a test message sat through when a server expecting TLS
    /// from the first byte was set on a port other than 465: each side waited for the other to speak.
    /// </summary>
    internal static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(30);

    private static async Task Send(SmtpClient client, SecureSocketOptions security, MimeMessage message,
        string host, int port, string? username, string? password, string from, TimeSpan connectLimit)
    {
        using (var limit = new CancellationTokenSource(connectLimit))
        {
            try
            {
                await client.ConnectAsync(host, port, security, limit.Token);
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The mail server {host}:{port} didn't answer within {connectLimit.TotalSeconds:0} seconds. Either it can't be reached, " +
                    "or it expects TLS from the first byte: Bitween does that only on port 465, and on any other port " +
                    "starts plainly and asks for STARTTLS.");
            }
        }

        // A relay that accepts unauthenticated mail from inside the network is a normal setup, so
        // only authenticate when a password was actually supplied.
        if (!string.IsNullOrWhiteSpace(password))
        {
            // Refusing beats sending the credential over a connection anyone on the path can read.
            if (!client.IsSecure)
                throw new InvalidOperationException(
                    "The SMTP handler will not send a password over an unencrypted connection. " +
                    "Set UseTls to true, or clear the password if the relay does not need one.");

            await client.AuthenticateAsync(string.IsNullOrWhiteSpace(username) ? from : username, password);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    internal static void AddAddresses(InternetAddressList list, string? addresses)
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
    /// Non-JSON payloads are normal for a pipeline handler - a CSV or a flat file on its way out -
    /// and those have no fields to substitute. A broken template still throws, so a typo in a
    /// placeholder is not quietly emailed as literal text.
    /// </remarks>
    internal static string Fill(string template, string payload) => ScribanJsonHelper.FillIfJson(template, payload);

    /// <summary>
    /// Whether a server certificate should be accepted, given what validation found wrong with it.
    /// </summary>
    /// <remarks>
    /// Only one defect is tolerated: a revocation status that could not be established, because the
    /// CA's OCSP or CRL server was unreachable. A certificate the CA has revoked, an untrusted root,
    /// a wrong hostname and an expired certificate are all still refused - as is any chain flag not
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
}
