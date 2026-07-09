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

    [DefaultValue(50)]
    [Description("Maximum number of messages to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode message contents (e.g. utf8).")]
    public string ResponseEncoding { get; set; } = "utf8";
}
