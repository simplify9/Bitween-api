using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.SlackMessageHandler;

/// <summary>
/// The Slack channel type: posts a notification to a Slack channel, laid out in Block Kit, through
/// an incoming webhook.
/// </summary>
public class NativeSlackMessageHandler(IHttpClientFactory httpClientFactory, DestinationCircuits? circuits = null)
    : INativeInfolinkHandler, IChannelMessageAdapter
{
    readonly DestinationCircuits circuits = circuits ?? DestinationCircuits.None;

    private SlackMessageInput _options = new();

    public string Name => nameof(NativeSlackMessageHandler);

    public Type StartupValuesType => typeof(SlackMessageInput);

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<SlackMessageInput>();
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var message = ChannelMessage.Parse(xchangeFile.Data);
        await Webhook.Post(httpClientFactory, circuits, _options.WebhookUrl, SlackBlocks.Render(message), "Slack");
        return new XchangeFile(message.Title, xchangeFile.Filename);
    }
}
