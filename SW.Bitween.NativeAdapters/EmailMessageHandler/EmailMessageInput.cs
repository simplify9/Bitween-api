using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.EmailMessageHandler;

public class EmailMessageInput
{
    [Required]
    [Description("Recipients, separated by commas.")]
    public string To { get; set; } = string.Empty;

    [Description("Carbon-copy recipients, separated by commas.")]
    public string? Cc { get; set; }

    [Description("Blind carbon-copy recipients, separated by commas.")]
    public string? Bcc { get; set; }
}
