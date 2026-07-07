using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.Pop3Receiver;

public class Pop3ReceiverInput
{
    [Required]
    public string Host { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Secure]
    public string Password { get; set; } = string.Empty;

    [DefaultValue(50)]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    public string ResponseEncoding { get; set; } = "utf8";
}
