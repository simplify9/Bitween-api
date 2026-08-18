using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.SmtpHandler;

public class SmtpHandlerInput
{
    [Required]
    [Description("SMTP server hostname.")]
    public string Host { get; set; } = string.Empty;

    [DefaultValue(587)]
    [Description("SMTP server port. 587 for STARTTLS, 465 for implicit SSL, 25 for an unencrypted relay.")]
    public int Port { get; set; } = 587;

    [Description("SMTP username. Leave empty to authenticate as the From address, or for a relay that needs no credentials.")]
    public string? Username { get; set; }

    [Secure]
    [Description("SMTP password. Leave empty for a relay that needs no credentials.")]
    public string? Password { get; set; }

    [DefaultValue(true)]
    [Description("Encrypt the connection, choosing STARTTLS or SSL to match the port. Turn off only for an internal relay with no TLS.")]
    public bool UseTls { get; set; } = true;

    [Required]
    [Description("Address the message is sent from.")]
    public string From { get; set; } = string.Empty;

    [Description("Display name shown beside the From address, e.g. Bitween Alerts.")]
    public string? FromName { get; set; }

    [Required]
    [Description("Recipients, separated by commas.")]
    public string To { get; set; } = string.Empty;

    [Description("Carbon-copy recipients, separated by commas.")]
    public string? Cc { get; set; }

    [Description("Blind carbon-copy recipients, separated by commas.")]
    public string? Bcc { get; set; }

    [Required]
    [Description(
        "Subject line. Placeholders in the incoming payload are substituted, e.g. " +
        "'Retries stopped for {{ SubscriptionName }}'.")]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [Description(
        "Message body. Uses the same template syntax as the JSON mapper, so payload fields can be " +
        "referenced directly, e.g. '{{ GroupName }} used all {{ MaxAttemptsTotal }} retries.'")]
    public string Body { get; set; } = string.Empty;

    [DefaultValue(true)]
    [Description("Send the body as HTML. Turn off to send it as plain text.")]
    public bool IsHtml { get; set; } = true;
}
