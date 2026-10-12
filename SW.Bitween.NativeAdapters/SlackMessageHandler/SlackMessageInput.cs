using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.SlackMessageHandler;

public class SlackMessageInput
{
    [Required]
    [Secure]
    [Description(
        "A Slack incoming webhook address (https://hooks.slack.com/services/...), made for the channel the " +
        "messages go to. Anyone with it can post there, so it is kept hidden.")]
    public string WebhookUrl { get; set; } = string.Empty;
}
