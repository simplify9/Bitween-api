using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.Pop3Receiver;

public class Pop3ReceiverInput
{
    [Required]
    [Description("POP3 server hostname.")]
    public string Host { get; set; } = string.Empty;

    [Required]
    [Description("POP3 account username.")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Secure]
    [Description("POP3 account password.")]
    public string Password { get; set; } = string.Empty;

    [DefaultValue("implicit")]
    [Description("implicit for POP3 over TLS from the first byte (usually port 995), or explicit for POP3 that upgrades the connection with STLS (usually port 110). The password is never sent unencrypted.")]
    public string Encryption { get; set; } = "implicit";

    [Description("The server's port. 995 with implicit TLS, 110 with explicit, when empty.")]
    public int? Port { get; set; }

    [Description("The SHA-256 thumbprint of the server's certificate, in hex. When set, exactly that certificate is trusted, a self-signed one included, and any other is refused before credentials are sent. When empty, the certificate must be valid.")]
    public string? CertificateThumbprint { get; set; }

    [DefaultValue(50)]
    [Description("Maximum number of messages to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode message contents (e.g. utf8).")]
    public string ResponseEncoding { get; set; } = "utf8";
}
