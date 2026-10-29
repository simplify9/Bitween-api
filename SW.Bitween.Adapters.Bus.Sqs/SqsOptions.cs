namespace SW.Bitween.Adapters.Bus.Sqs;

/// <summary>
/// Bound by name from the DataSource's properties. The attributes are what Bitween's data source
/// form is built from - see AdapterSettingAttribute for why the UI no longer keeps its own copy of
/// this list.
/// </summary>
[AdapterSettings(
    Kind = "Broker",
    Label = "Amazon SQS",
    Description = "An SQS queue, including the one an Amazon Selling Partner notification "
                  + "subscription delivers to.")]
public class SqsOptions
{
    /// <summary>
    /// False creates the client but starts no pollers. This is what a connection test runs as:
    /// without it, testing a data source would start receiving from the customer's queue.
    /// </summary>
    [AdapterSetting(Hidden = true)]
    public bool Consume { get; set; } = true;

    [AdapterSetting(Required = true, Default = "eu-west-1",
        Hint = "The queue's region, not Bitween's.")]
    public string Region { get; set; } = "eu-west-1";

    /// <summary>
    /// Leave both blank to use the ambient credential chain - instance profile, IRSA, or the
    /// environment. That is the right answer on AWS; explicit keys are for everything else.
    /// </summary>
    [AdapterSetting(Secret = true,
        Hint = "Leave both keys blank on AWS to use the instance profile or IRSA instead.")]
    public string AccessKeyId { get; set; }

    [AdapterSetting(Secret = true)]
    public string SecretAccessKey { get; set; }

    /// <summary>Override for LocalStack or ElasticMQ in development.</summary>
    [AdapterSetting(Hint = "Override for LocalStack or ElasticMQ in development. Leave empty for AWS.")]
    public string ServiceUrl { get; set; }

    /// <summary>Comma-separated queue URLs, supplied by the gateways bound to this data source.</summary>
    [AdapterSetting(Hidden = true)]
    public string Endpoints { get; set; }

    /// <summary>Long polling. 20 is the maximum and the only sensible value - 0 burns money and API calls.</summary>
    [AdapterSetting(Default = "20",
        Hint = "Long polling, in seconds. 20 is the maximum and the only sensible value - 0 burns "
               + "money and API calls.")]
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>Max 10 per receive; that is an SQS limit, not a choice.</summary>
    [AdapterSetting(Default = "10", Hint = "10 is SQS's own maximum, not a choice.")]
    public int MaxMessagesPerReceive { get; set; } = 10;

    /// <summary>
    /// How long a received message stays invisible to other consumers. It must exceed the time
    /// Bitween takes to persist, or the message is redelivered while still being handled.
    /// </summary>
    [AdapterSetting(Default = "60",
        Hint = "Must exceed how long Bitween takes to persist a message, or SQS redelivers one "
               + "already being handled.")]
    public int VisibilityTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// SP-API wraps its notifications in an envelope. On, the adapter forwards only the payload
    /// and promotes notificationType and the SP-API metadata into headers.
    /// </summary>
    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" },
        Hint = "true unwraps the SP-API envelope so subscriptions see the notification payload itself.")]
    public bool UnwrapSellingPartnerNotification { get; set; }
}
