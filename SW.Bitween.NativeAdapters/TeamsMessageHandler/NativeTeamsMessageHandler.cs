using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.TeamsMessageHandler;

/// <summary>
/// The Microsoft Teams channel type: posts a notification to a Teams channel as an Adaptive Card,
/// through a Teams Workflows webhook.
/// </summary>
/// <remarks>
/// Workflows is the only kind of Teams webhook left: Microsoft retired the Office 365 "Incoming
/// Webhook" connector, and its outlook.office.com addresses no longer accept posts.
/// </remarks>
public class NativeTeamsMessageHandler(IHttpClientFactory httpClientFactory, DestinationCircuits? circuits = null)
    : INativeInfolinkHandler, IChannelMessageAdapter
{
    readonly DestinationCircuits circuits = circuits ?? DestinationCircuits.None;

    private TeamsMessageInput _options = new();

    public string Name => nameof(NativeTeamsMessageHandler);

    public Type StartupValuesType => typeof(TeamsMessageInput);

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<TeamsMessageInput>();
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var message = ChannelMessage.Parse(xchangeFile.Data);
        await Webhook.Post(httpClientFactory, circuits, _options.WebhookUrl, TeamsCard.Render(message), "Teams");
        return new XchangeFile(message.Title, xchangeFile.Filename);
    }
}
