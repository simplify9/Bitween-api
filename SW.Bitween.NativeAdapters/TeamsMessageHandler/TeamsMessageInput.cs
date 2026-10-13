using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.TeamsMessageHandler;

public class TeamsMessageInput
{
    [Required]
    [Secure]
    [Description(
        "A Teams Workflows webhook address. In Teams, add the \"Post to a channel when a webhook request " +
        "is received\" workflow to the channel, then copy the address it gives you. Anyone with it can post " +
        "to the channel, so it is kept hidden.")]
    public string WebhookUrl { get; set; } = string.Empty;
}
