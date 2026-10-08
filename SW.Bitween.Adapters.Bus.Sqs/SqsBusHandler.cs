using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using SW.Serverless.Sdk.Resident;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Bus.Sqs;

/// <summary>
/// Amazon SQS as a Bitween bus provider.
///
/// SQS is a poll-based queue, not a push broker, so the shape differs from RabbitMQ in one way
/// that matters: there is no ack, only DELETE. A message stays invisible for the visibility
/// timeout and reappears if it is not deleted — which is the same at-least-once contract by a
/// different mechanism, and maps cleanly onto persist-then-acknowledge:
///
///     receive -> PublishAsync to the host -> host persists -> ONLY THEN DeleteMessage
///
/// Reject or crash in between and SQS redelivers when the visibility timeout expires. So
/// VisibilityTimeoutSeconds must exceed how long Bitween takes to persist, or a message is
/// redelivered while the first copy is still being handled.
///
/// WHY THIS ONE MATTERS BEYOND SQS ITSELF: the Amazon Selling Partner API delivers notifications
/// by publishing to an SQS queue that you own. You create the queue, grant SP-API permission to
/// send to it, then subscribe notification types to that destination. So this adapter is the
/// transport for SP-API notifications — ORDER_CHANGE, LISTINGS_ITEM_STATUS_CHANGE, REPORT_PROCESSING_FINISHED
/// and the rest — and <see cref="SqsOptions.UnwrapSellingPartnerNotification"/> handles the
/// envelope they arrive in. The SP-API request/response calls themselves are ordinary HTTPS and
/// belong in a mapper or handler, not here.
/// </summary>
// Two roles, one package. "bus" is what makes it configurable as a broker connection; "handler"
// is what puts it in the delivery picker, because the same resident instance both drains a
// customer's queue and sends back to one.
[AdapterKind("bus")]
[AdapterKind("handler")]
public class SqsBusHandler(IOptions<SqsOptions> options, ILogger<SqsBusHandler> logger)
    : IResidentAdapter, IInfolinkHandler
{
    private readonly SqsOptions _options = options.Value;

    private IAmazonSQS _sqs;
    private IAdapterContext _context;
    private CancellationTokenSource _stopping;
    private readonly List<Task> _pollers = new();
    private readonly List<string> _endpoints = new();

    private long _received, _deleted, _returned, _failed, _sent;
    private DateTimeOffset? _lastMessageOn;
    private string _lastError;
    private volatile string _state = "Starting";

    // ---------------------------------------------------------------- lifecycle

    public Task StartAsync(IAdapterContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _endpoints.AddRange((_options.Endpoints ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        _sqs = CreateClient(_options);

        _state = _endpoints.Count == 0 ? "Idle" : "Connected";
        logger.LogInformation("SQS client for {Region}, polling {Count} queue(s) with {Wait}s long poll.",
            _options.Region, _endpoints.Count, _options.WaitTimeSeconds);

        // One poller per queue. Long polling means these are cheap: a blocked receive costs
        // nothing until a message arrives or the wait expires. Skipped entirely for a connection
        // test, which still checks every endpoint because TestConnection reads _endpoints.
        if (_options.Consume)
            foreach (var endpoint in _endpoints)
                _pollers.Add(Task.Run(() => PollAsync(endpoint, _stopping.Token)));
        else
            _state = "Idle";

        return Task.CompletedTask;
    }

    private static IAmazonSQS CreateClient(SqsOptions options)
    {
        var config = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }

        // No keys means the ambient chain — instance profile, IRSA, environment. That is the
        // correct production answer on AWS, and explicit keys are the exception.
        return string.IsNullOrWhiteSpace(options.AccessKeyId)
            ? new AmazonSQSClient(config)
            : new AmazonSQSClient(
                new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state = "Draining";
        _stopping?.Cancel();

        // In-flight messages are simply not deleted, so SQS redelivers them after the visibility
        // timeout. Nothing is lost by stopping mid-batch.
        if (_pollers.Count > 0)
            await Task.WhenAny(Task.WhenAll(_pollers), Task.Delay(5000, cancellationToken));

        _sqs?.Dispose();
        _state = "Stopped";
    }

    public async Task<AdapterStatus> GetStatusAsync()
    {
        var status = new AdapterStatus
        {
            Connected = _sqs != null && _state != "Disconnected",
            State = _received == 0 && _state == "Connected" ? "Idle" : _state,
            LastMessageOn = _lastMessageOn,
            LastError = _lastError,
            InFlight = Math.Max(0, _received - _deleted - _returned - _failed)
        };

        status.Details["region"] = _options.Region;
        status.Details["endpoints"] = string.Join(",", _endpoints);
        status.Details["visibilityTimeout"] = _options.VisibilityTimeoutSeconds.ToString();
        status.Details["received"] = _received.ToString();
        status.Details["deleted"] = _deleted.ToString();
        status.Details["returned"] = _returned.ToString();
        status.Details["failed"] = _failed.ToString();
        status.Details["sent"] = _sent.ToString();

        // ApproximateNumberOfMessages is the SQS equivalent of queue depth, and the only backlog
        // signal available without CloudWatch.
        foreach (var endpoint in _endpoints)
        {
            var depth = await DepthAsync(endpoint);
            if (depth != null) status.Details[$"depth:{Short(endpoint)}"] = depth;
        }

        return status;
    }

    private static string Short(string queueUrl) => queueUrl[(queueUrl.LastIndexOf('/') + 1)..];

    private async Task<string> DepthAsync(string queueUrl)
    {
        try
        {
            var attributes = await _sqs.GetQueueAttributesAsync(queueUrl,
                new List<string> { "ApproximateNumberOfMessages" });
            return attributes.ApproximateNumberOfMessages.ToString();
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- ingress

    private async Task PollAsync(string queueUrl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = Math.Clamp(_options.MaxMessagesPerReceive, 1, 10),
                    WaitTimeSeconds = Math.Clamp(_options.WaitTimeSeconds, 0, 20),
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                    MessageAttributeNames = new List<string> { "All" },
                    MessageSystemAttributeNames = new List<string> { "All" }
                }, ct);

                _state = "Connected";

                foreach (var message in response.Messages ?? new List<Message>())
                {
                    if (ct.IsCancellationRequested) return;
                    await HandleAsync(queueUrl, message, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _state = "Disconnected";
                _lastError = ex.Message;
                logger.LogError(ex, "Polling {Queue} failed.", Short(queueUrl));

                // Back off rather than hammering a failing endpoint; the supervisor decides
                // whether this is terminal.
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task HandleAsync(string queueUrl, Message message, CancellationToken ct)
    {
        Interlocked.Increment(ref _received);

        try
        {
            var headers = new Dictionary<string, string>
            {
                ["sqs.messageId"] = message.MessageId,
                ["sqs.queue"] = Short(queueUrl)
            };

            foreach (var attribute in message.MessageAttributes ?? new Dictionary<string, MessageAttributeValue>())
                headers[$"sqs.attr.{attribute.Key}"] = attribute.Value?.StringValue ?? "";

            var body = message.Body ?? "";

            if (_options.UnwrapSellingPartnerNotification)
                body = UnwrapSpApi(body, headers);

            var result = await _context.PublishAsync(
                System.Text.Encoding.UTF8.GetBytes(body),
                // MessageId is unique per message but NOT stable across redelivery of the same
                // logical message on a standard queue, so a SequenceNumber or the SP-API
                // notification id is preferred where present.
                dedupeKey: headers.TryGetValue("spapi.notificationId", out var notificationId)
                    ? $"spapi:{notificationId}"
                    : $"sqs:{Short(queueUrl)}:{message.MessageId}",
                endpoint: queueUrl,
                headers: headers,
                contentType: "application/json",
                cancellationToken: ct);

            if (result.Accepted)
            {
                // The SQS equivalent of an ack. Until this call, the message is merely invisible.
                await _sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, ct);
                Interlocked.Increment(ref _deleted);
                _lastMessageOn = DateTimeOffset.UtcNow;
                _context.Metric("bitween.bus.sqs.deleted", 1);
            }
            else
            {
                // Back on the queue after a short wait rather than the full visibility timeout, so a
                // transient Bitween failure retries in seconds rather than minutes.
                await ReturnToQueueAsync(queueUrl, message, ct);
                Interlocked.Increment(ref _returned);
                _lastError = result.Error;
                logger.LogWarning("Bitween rejected {MessageId} from {Queue}: {Error}. Returned to the queue.",
                    message.MessageId, Short(queueUrl), result.Error);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            _lastError = ex.Message;
            logger.LogError(ex, "Failed to hand {MessageId} to Bitween.", message.MessageId);
            try { await ReturnToQueueAsync(queueUrl, message, CancellationToken.None); } catch { }
        }
    }

    /// <summary>
    /// Puts the message back after a wait that grows with each receive: five seconds a time, up to
    /// five minutes. It used to be made visible again at once, so a message that always fails was
    /// received again straight away, over and over, until the queue's redrive policy moved it — or
    /// for good, on a queue without one.
    /// </summary>
    private Task ReturnToQueueAsync(string queueUrl, Message message, CancellationToken ct)
    {
        var receives = message.Attributes != null &&
                       message.Attributes.TryGetValue("ApproximateReceiveCount", out var count) &&
                       int.TryParse(count, out var parsed)
            ? parsed
            : 1;
        return _sqs.ChangeMessageVisibilityAsync(queueUrl, message.ReceiptHandle, Math.Min(receives * 5, 300), ct);
    }

    /// <summary>
    /// SP-API notifications arrive wrapped: notificationType, notificationVersion, payloadVersion,
    /// eventTime, notificationMetadata and the actual payload. Forwarding the whole envelope would
    /// make every Bitween document schema carry Amazon's wrapper, so promote the metadata to
    /// headers and pass the payload through.
    /// </summary>
    private string UnwrapSpApi(string body, IDictionary<string, string> headers)
    {
        try
        {
            var envelope = JObject.Parse(body);
            var payload = envelope["payload"];
            if (payload == null) return body;

            if (envelope["notificationType"]?.ToString() is { Length: > 0 } type)
                headers["spapi.notificationType"] = type;
            if (envelope["eventTime"]?.ToString() is { Length: > 0 } eventTime)
                headers["spapi.eventTime"] = eventTime;

            var metadata = envelope["notificationMetadata"];
            if (metadata?["notificationId"]?.ToString() is { Length: > 0 } notificationId)
                headers["spapi.notificationId"] = notificationId;
            if (metadata?["subscriptionId"]?.ToString() is { Length: > 0 } subscriptionId)
                headers["spapi.subscriptionId"] = subscriptionId;

            return payload.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Exception ex)
        {
            // Not fatal: forward the raw body and let a mapper deal with it.
            logger.LogWarning(ex, "Body did not look like an SP-API notification; forwarding it whole.");
            return body;
        }
    }

    // ---------------------------------------------------------------- commands

    public async Task<object> Publish(PublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Endpoint))
            throw new ArgumentException("Endpoint (the queue URL) is required.");

        var send = new SendMessageRequest { QueueUrl = request.Endpoint, MessageBody = request.Body ?? "" };

        // FIFO queues require a group id, and reject the request without one.
        if (request.Endpoint.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            send.MessageGroupId = request.GroupId ?? "bitween";
            if (!string.IsNullOrWhiteSpace(request.DeduplicationId))
                send.MessageDeduplicationId = request.DeduplicationId;
        }

        var response = await _sqs.SendMessageAsync(send, _stopping.Token);
        Interlocked.Increment(ref _sent);

        return new { messageId = response.MessageId, sequenceNumber = response.SequenceNumber };
    }

    /// <summary>
    /// Egress through the PIPELINE: a subscription's delivery stage, sending the message it was
    /// given to a queue these credentials can reach.
    ///
    /// <see cref="Publish"/> has existed since this adapter did, and nothing could reach it —
    /// Bitween's pipeline calls <c>Handle</c> on a handler, and this class had none. This is the
    /// two joined up; the sending itself is unchanged.
    ///
    /// The queue URL is the SUBSCRIPTION's, not the connection's: one instance serves every
    /// gateway on these credentials, so it travels with the call. Deliberately not restricted to
    /// the queues this data source consumes — the common case for egress is one it does not.
    /// </summary>
    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var endpoint = _context?.ValueOf("Endpoint");

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "This delivery has no Endpoint, so there is nowhere to send. Set it to the queue "
                + "URL — the full https://sqs.<region>.amazonaws.com/<account>/<name>, which is "
                + "what the SDK addresses a queue by.");

        var receipt = await Publish(new PublishRequest
        {
            Endpoint = endpoint,

            // Only read by a FIFO queue, and required by one. Defaulting the group to the
            // exchange id would put every message in its own group and lose the ordering a FIFO
            // queue exists for, so it is left to configuration.
            GroupId = _context?.ValueOf("GroupId"),

            // The exchange id makes a redelivery recognisable as the same message, which is what
            // a FIFO queue deduplicates on within its five-minute window.
            DeduplicationId = _context?.ValueOf("DeduplicationId") ?? _context?.ValueOf("xchangeid"),

            Body = xchangeFile?.Data ?? ""
        });

        return new XchangeFile(JsonConvert.SerializeObject(receipt), xchangeFile?.Filename);
    }

    public async Task<object> TestConnection()
    {
        var steps = new List<object>();
        try
        {
            using var probe = CreateClient(_options);

            var listed = await probe.ListQueuesAsync(new ListQueuesRequest { MaxResults = 1 });
            steps.Add(new { step = "credentials", ok = true, detail = _options.Region });

            foreach (var endpoint in _endpoints)
            {
                try
                {
                    var attributes = await probe.GetQueueAttributesAsync(endpoint,
                        new List<string> { "ApproximateNumberOfMessages", "VisibilityTimeout" });

                    steps.Add(new
                    {
                        step = $"queue:{Short(endpoint)}",
                        ok = true,
                        detail = $"{attributes.ApproximateNumberOfMessages} message(s), " +
                                 $"visibility {attributes.VisibilityTimeout}s"
                    });

                    // A visibility timeout shorter than Bitween's persist time means duplicate
                    // processing, so say so before it happens in production.
                    if (attributes.VisibilityTimeout < 30)
                        steps.Add(new
                        {
                            step = $"queue:{Short(endpoint)}:warning",
                            ok = true,
                            detail = $"Visibility timeout is {attributes.VisibilityTimeout}s. " +
                                     "Anything under ~30s risks redelivery while Bitween is still persisting."
                        });
                }
                catch (Exception ex)
                {
                    steps.Add(new { step = $"queue:{Short(endpoint)}", ok = false, detail = ex.Message });
                    return new { ok = false, steps };
                }
            }

            return new { ok = true, steps };
        }
        catch (Exception ex)
        {
            steps.Add(new { step = "failed", ok = false, detail = ex.Message });
            return new { ok = false, steps };
        }
    }

    /// <summary>Lists the queues these credentials can see, for the "pick a queue" step in the UI.</summary>
    public async Task<object> Discover()
    {
        try
        {
            var listed = await _sqs.ListQueuesAsync(new ListQueuesRequest { MaxResults = 100 });
            return new
            {
                region = _options.Region,
                queues = listed.QueueUrls.Select(url => new { url, name = Short(url) }),
                consuming = _endpoints
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message, hint = "ListQueues needs sqs:ListQueues on the principal." };
        }
    }

    public Task<object> GetStats() => Task.FromResult<object>(new
    {
        received = _received, deleted = _deleted, returned = _returned, failed = _failed, sent = _sent,
        endpoints = _endpoints, visibilityTimeoutSeconds = _options.VisibilityTimeoutSeconds
    });

    public class PublishRequest
    {
        /// <summary>The queue URL.</summary>
        public string Endpoint { get; set; }
        public string Body { get; set; }
        public string GroupId { get; set; }
        public string DeduplicationId { get; set; }
    }
}
