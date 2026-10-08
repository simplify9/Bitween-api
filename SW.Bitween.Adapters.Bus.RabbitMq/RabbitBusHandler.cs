using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Newtonsoft.Json;
using RabbitMQ.Client.Events;
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using SW.Serverless.Sdk.Resident;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Bus.RabbitMq;

/// <summary>
/// Bitween's external RabbitMQ bus provider — a broker that is NOT the internal one, owned by
/// someone else, whose queues Bitween consumes and publishes to.
///
/// The ack ordering is the contract:
///
///     delivery -> PublishAsync to the host -> host persists the Xchange -> ack returns
///              -> ONLY THEN BasicAck
///
/// A host rejection becomes BasicNack(requeue: true), so a Bitween outage does not lose the
/// customer's messages — it just stops draining their queue, which is the correct failure.
/// </summary>
// Two roles, one package. "bus" is what makes it configurable as a broker connection; "handler"
// is what puts it in the delivery picker, because the same resident instance both consumes a
// customer's queue and publishes back to one. Declared rather than encoded in the id: reclassifying
// by rename would break every gateway that stores it.
[AdapterKind("bus")]
[AdapterKind("handler")]
public class RabbitBusHandler(IOptions<RabbitOptions> options, ILogger<RabbitBusHandler> logger)
    : IResidentAdapter, IInfolinkHandler
{
    private readonly RabbitOptions _options = options.Value;

    private IAdapterContext _context;
    private IConnection _connection;
    private IChannel _consumeChannel;
    private IChannel _publishChannel;

    // Deliveries are handled concurrently, up to the prefetch, so acks, nacks and the shutdown
    // calls on one channel are serialised through these. Two gates rather than one: a publish must
    // never queue behind a slow ack. Async, because every channel operation is in RabbitMQ.Client 7.
    private readonly SemaphoreSlim _consumeGate = new(1, 1);
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private CancellationTokenSource _stopping;

    private readonly List<string> _endpoints = new();
    private readonly Dictionary<string, string> _consumerTags = new();

    private long _received, _acked, _nacked, _failed, _published, _poisoned;

    // Failed attempts per message, for brokers that don't count them (only quorum queues send
    // x-delivery-count). Keyed by message id, or by a hash of the body when there is none.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _attempts = new();

    private DateTimeOffset? _lastMessageOn;
    private string _lastError;
    private volatile string _state = "Starting";

    // ---------------------------------------------------------------- lifecycle

    public async Task StartAsync(IAdapterContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _endpoints.AddRange((_options.Endpoints ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password ?? "",
            VirtualHost = _options.VirtualHost,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(15),

            // The supervisor owns restart policy — backoff, crash-loop quarantine, health
            // write-back. A second, hidden recovery loop in here would fight it.
            AutomaticRecoveryEnabled = false,

            // Deliveries were handed to the thread pool one task each, so up to the prefetch ran at
            // once. This keeps that, and keeps each delivery's body valid until its handler is done.
            ConsumerDispatchConcurrency = Math.Max((ushort)1, _options.Prefetch)
        };
        if (_options.UseSsl) factory.Ssl = new SslOption { Enabled = true, ServerName = _options.Host };

        _connection = await factory.CreateConnectionAsync($"bitween-{context.InstanceKey}", cancellationToken);
        _connection.ConnectionShutdownAsync += (_, e) =>
        {
            _state = "Disconnected";
            _lastError = $"{e.ReplyCode} {e.ReplyText}";
            logger.LogWarning("Connection to {Host} closed: {Reason}", _options.Host, e.ReplyText);
            return Task.CompletedTask;
        };

        // With confirmations tracked, a publish completes only once the broker has confirmed it,
        // and throws when the broker refuses it or — mandatory — routes it nowhere.
        _publishChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: _options.PublisherConfirms,
                publisherConfirmationTrackingEnabled: _options.PublisherConfirms),
            cancellationToken);
        _consumeChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _consumeChannel.BasicQosAsync(0, _options.Prefetch, global: false, cancellationToken);

        await DeclareTopologyAsync(_consumeChannel, cancellationToken);

        // Manage-only: the connection is up and the topology is declared, but nothing is consumed.
        // TestConnection still checks every endpoint, because it reads _endpoints rather than the
        // consumers — so a test proves the queues exist without draining them.
        if (!_options.Consume)
        {
            _state = "Idle";
            logger.LogInformation("Connected to {Host} without consuming (Consume=false).", _options.Host);
            return;
        }

        foreach (var endpoint in _endpoints)
        {
            var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(endpoint, delivery);

            // autoAck: false is what makes persist-then-ack possible at all.
            _consumerTags[endpoint] = await _consumeChannel.BasicConsumeAsync(endpoint, autoAck: false, consumer,
                cancellationToken);
        }

        _state = _endpoints.Count == 0 ? "Idle" : "Connected";
        logger.LogInformation("Connected to {Host}:{Port}{VHost}, consuming {Count} endpoint(s) with prefetch {Prefetch}.",
            _options.Host, _options.Port, _options.VirtualHost, _endpoints.Count, _options.Prefetch);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var mode = (_options.DeclareMode ?? "assert").ToLowerInvariant();
        if (mode == "none") return;

        if (mode == "create" && !string.IsNullOrWhiteSpace(_options.Exchange))
            await channel.ExchangeDeclareAsync(_options.Exchange, _options.ExchangeType ?? "topic",
                durable: _options.Durable, autoDelete: false, cancellationToken: cancellationToken);

        foreach (var endpoint in _endpoints)
        {
            if (mode == "assert")
            {
                // Throws if it does not exist, which is what we want: better to fail at start than
                // to silently create a queue on someone else's broker.
                await channel.QueueDeclarePassiveAsync(endpoint, cancellationToken);
                continue;
            }

            var arguments = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(_options.QueueType)) arguments["x-queue-type"] = _options.QueueType;

            await channel.QueueDeclareAsync(endpoint, durable: _options.Durable, exclusive: false,
                autoDelete: false, arguments: arguments.Count == 0 ? null : arguments,
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Exchange))
                await channel.QueueBindAsync(endpoint, _options.Exchange, _options.RoutingKey ?? endpoint,
                    cancellationToken: cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _state = "Draining";
        _stopping?.Cancel();

        await _consumeGate.WaitAsync(CancellationToken.None);
        try
        {
            foreach (var tag in _consumerTags.Values)
                try { if (_consumeChannel != null) await _consumeChannel.BasicCancelAsync(tag); } catch { }

            try { if (_consumeChannel != null) await _consumeChannel.CloseAsync(); } catch { }
        }
        finally { _consumeGate.Release(); }

        await _publishGate.WaitAsync(CancellationToken.None);
        try { if (_publishChannel != null) await _publishChannel.CloseAsync(); } catch { }
        finally { _publishGate.Release(); }

        try { if (_connection != null) await _connection.CloseAsync(TimeSpan.FromSeconds(3)); } catch { }
        _connection?.Dispose();

        _state = "Stopped";
    }

    public async Task<AdapterStatus> GetStatusAsync()
    {
        var status = new AdapterStatus
        {
            Connected = _connection?.IsOpen == true,
            State = _connection?.IsOpen != true ? "Disconnected"
                  : _received == 0 ? "Idle" : _state,
            LastMessageOn = _lastMessageOn,
            LastError = _lastError,
            InFlight = Math.Max(0, _received - _acked - _nacked - _failed)
        };

        status.Details["host"] = $"{_options.Host}:{_options.Port}{_options.VirtualHost}";
        status.Details["endpoints"] = string.Join(",", _endpoints);
        status.Details["prefetch"] = _options.Prefetch.ToString();
        status.Details["received"] = _received.ToString();
        status.Details["acked"] = _acked.ToString();
        status.Details["nacked"] = _nacked.ToString();
        status.Details["failed"] = _failed.ToString();
        status.Details["published"] = _published.ToString();

        status.Details["poisoned"] = _poisoned.ToString();

        foreach (var endpoint in _endpoints)
            status.Details[$"depth:{endpoint}"] = (await DepthAsync(endpoint))?.ToString() ?? "?";

        return status;
    }

    private async Task<uint?> DepthAsync(string queue)
    {
        try
        {
            await using var probe = await _connection.CreateChannelAsync();
            return (await probe.QueueDeclarePassiveAsync(queue)).MessageCount;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- ingress

    private async Task HandleAsync(string endpoint, BasicDeliverEventArgs delivery)
    {
        Interlocked.Increment(ref _received);

        try
        {
            var headers = new Dictionary<string, string>
            {
                ["rabbit.exchange"] = delivery.Exchange,
                ["rabbit.routingKey"] = delivery.RoutingKey,
                ["rabbit.redelivered"] = delivery.Redelivered.ToString()
            };
            if (delivery.BasicProperties?.MessageId is { Length: > 0 } messageId)
                headers["rabbit.messageId"] = messageId;

            var result = await _context.PublishAsync(
                delivery.Body,
                // The broker's own message id, and nothing else. NOT the delivery tag — tags are
                // per channel and restart at 1 on every reconnect — and NOT a content hash: two
                // messages that legitimately carry the same body are two messages, and hashing
                // them would silently drop the second for the whole deduplication window. An
                // unkeyed delivery is handled once per delivery, which is the honest answer when
                // the publisher gave us nothing to identify it by.
                dedupeKey: delivery.BasicProperties?.MessageId is { Length: > 0 } id
                    ? $"rabbit:{_options.Host}:{endpoint}:{id}"
                    : WarnUnkeyed(endpoint),
                endpoint: endpoint,
                headers: headers,
                contentType: delivery.BasicProperties?.ContentType ?? "application/json",
                cancellationToken: _stopping.Token);

            if (result.Accepted)
            {
                await OnConsumeChannelAsync(c => c.BasicAckAsync(delivery.DeliveryTag, multiple: false));
                _attempts.TryRemove(AttemptKey(endpoint, delivery), out _);
                Interlocked.Increment(ref _acked);
                _lastMessageOn = DateTimeOffset.UtcNow;
                _context.Metric("bitween.bus.rabbitmq.acked", 1);
            }
            else
            {
                Interlocked.Increment(ref _nacked);
                _lastError = result.Error;
                await RejectAsync(endpoint, delivery, result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            try { await OnConsumeChannelAsync(c => c.BasicNackAsync(delivery.DeliveryTag, false, requeue: true)); } catch { }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            _lastError = ex.Message;
            logger.LogError(ex, "Failed to hand a delivery from {Endpoint} to Bitween.", endpoint);
            try { await RejectAsync(endpoint, delivery, ex.Message); } catch { }
        }
    }

    /// <summary>
    /// A message Bitween did not take. It used to go straight back on the queue, so one that always
    /// fails came straight back too — round and round, as fast as the broker could deliver it,
    /// holding a prefetch slot and filling the log. Now each requeue waits a little longer, and
    /// after <see cref="RabbitOptions.MaxDeliveryAttempts"/> the poison action takes over.
    /// </summary>
    private async Task RejectAsync(string endpoint, BasicDeliverEventArgs delivery, string error)
    {
        var attempt = AttemptOf(endpoint, delivery);
        var action = (_options.PoisonAction ?? "requeue").ToLowerInvariant();

        if (_options.MaxDeliveryAttempts > 0 && attempt >= _options.MaxDeliveryAttempts && action != "requeue")
        {
            if (action == "park" && !string.IsNullOrWhiteSpace(_options.ParkingQueue))
            {
                await ParkAsync(endpoint, delivery, error, attempt);
                await OnConsumeChannelAsync(c => c.BasicAckAsync(delivery.DeliveryTag, multiple: false));
            }
            else
            {
                await OnConsumeChannelAsync(c => c.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false));
            }

            _attempts.TryRemove(AttemptKey(endpoint, delivery), out _);
            Interlocked.Increment(ref _poisoned);
            logger.LogError("A message from {Endpoint} failed {Attempts} times and was {Action}: {Error}",
                endpoint, attempt, action == "park" ? $"parked on {_options.ParkingQueue}" : "rejected", error);
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 10)), _stopping.Token);
        }
        catch (OperationCanceledException) { /* stopping: requeue straight away */ }

        await OnConsumeChannelAsync(c => c.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true));
        logger.LogWarning("Bitween rejected a message from {Endpoint} (attempt {Attempt}): {Error}. Requeued.",
            endpoint, attempt, error);
    }

    private int AttemptOf(string endpoint, BasicDeliverEventArgs delivery)
    {
        // Quorum queues count deliveries themselves, across restarts and nodes.
        if (delivery.BasicProperties?.Headers?.TryGetValue("x-delivery-count", out var counted) == true &&
            counted is not null && long.TryParse(counted.ToString(), out var deliveries))
            return (int)deliveries + 1;

        if (_attempts.Count > 100_000) _attempts.Clear();
        return _attempts.AddOrUpdate(AttemptKey(endpoint, delivery), 1, (_, n) => n + 1);
    }

    private static string AttemptKey(string endpoint, BasicDeliverEventArgs delivery) =>
        endpoint + ":" + (delivery.BasicProperties?.MessageId is { Length: > 0 } id
            ? id
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(delivery.Body.Span)));

    private async Task OnConsumeChannelAsync(Func<IChannel, ValueTask> action)
    {
        await _consumeGate.WaitAsync();
        try { await action(_consumeChannel); }
        finally { _consumeGate.Release(); }
    }

    private async Task ParkAsync(string endpoint, BasicDeliverEventArgs delivery, string error, int attempts)
    {
        var headers = new Dictionary<string, object>();
        if (delivery.BasicProperties?.Headers is { } original)
            foreach (var (key, value) in original) headers[key] = value;
        headers["x-bitween-error"] = error ?? "";
        headers["x-bitween-endpoint"] = endpoint;
        headers["x-bitween-attempts"] = attempts;

        var properties = new BasicProperties
        {
            ContentType = delivery.BasicProperties?.ContentType,
            MessageId = delivery.BasicProperties?.MessageId,
            DeliveryMode = DeliveryModes.Persistent,
            Headers = headers
        };

        await PublishConfirmedAsync("", _options.ParkingQueue, mandatory: false, properties, delivery.Body);
    }

    /// <summary>
    /// Publishes and, with confirmations on, returns only once the broker has confirmed it. Without
    /// that, a message the broker dropped — a full queue, a failover — was still recorded as
    /// delivered. A refusal, or a mandatory message routed nowhere, throws.
    /// </summary>
    private async Task PublishConfirmedAsync(string exchange, string routingKey, bool mandatory,
        BasicProperties properties, ReadOnlyMemory<byte> body)
    {
        await _publishGate.WaitAsync();
        try
        {
            await _publishChannel.BasicPublishAsync(exchange, routingKey, mandatory, properties, body);
        }
        catch (RabbitMQ.Client.Exceptions.PublishException ex)
        {
            throw new InvalidOperationException(ex.IsReturn
                ? "No queue received the message; the broker returned it as unroutable."
                : "The broker refused the message.", ex);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private long _unkeyed;

    private string WarnUnkeyed(string endpoint)
    {
        if (Interlocked.Increment(ref _unkeyed) == 1)
            logger.LogWarning(
                "A message arrived on {Endpoint} with no MessageId, so it cannot be deduplicated. " +
                "A redelivery of it will be processed again. Publishers should set one.", endpoint);

        return null;
    }

    // ---------------------------------------------------------------- commands

    /// <summary>
    /// Egress. Bitween does not have this on the internal gateway yet; an external provider gets
    /// it for free because the adapter owns the connection either way.
    /// </summary>
    public async Task<object> Publish(PublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Endpoint) && string.IsNullOrWhiteSpace(request?.Exchange))
            throw new ArgumentException("Either Endpoint or Exchange is required.");

        var body = System.Text.Encoding.UTF8.GetBytes(request.Body ?? "");
        var messageId = request.MessageId ?? Guid.NewGuid().ToString("N");

        var properties = new BasicProperties
        {
            ContentType = request.ContentType ?? "application/json",
            MessageId = messageId,
            DeliveryMode = _options.Durable ? DeliveryModes.Persistent : DeliveryModes.Transient
        };

        await PublishConfirmedAsync(
            exchange: request.Exchange ?? "",
            routingKey: request.Exchange == null ? request.Endpoint : request.RoutingKey ?? "",
            mandatory: _options.Mandatory,
            properties,
            body);

        Interlocked.Increment(ref _published);
        return new { messageId, bytes = body.Length };
    }

    /// <summary>
    /// Egress through the PIPELINE: a subscription's delivery stage, publishing the message it was
    /// given to a queue on this broker.
    ///
    /// <see cref="Publish"/> has existed since this adapter did, and nothing could reach it —
    /// Bitween's pipeline calls <c>Handle</c> on a handler, and this class had none, so an operator
    /// could consume from a customer's broker and had no way to answer on it. This is the two of
    /// them joined up; the publishing itself is unchanged.
    ///
    /// Where to send is the SUBSCRIPTION's business, not the connection's: one instance serves
    /// every gateway on this broker, so the endpoint travels with the call rather than with the
    /// process. Deliberately NOT restricted to the endpoints the data source consumes — the common
    /// case for egress is a queue Bitween does not drain.
    /// </summary>
    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var endpoint = _context?.ValueOf("Endpoint");
        var exchange = _context?.ValueOf("Exchange");

        if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(exchange))
            throw new InvalidOperationException(
                "This delivery has no Endpoint and no Exchange, so there is nowhere to publish. Set "
                + "Endpoint to a queue name, or Exchange (with an optional RoutingKey) to publish "
                + "through an exchange.");

        return PublishAsFile(xchangeFile, endpoint, exchange);
    }

    async Task<XchangeFile> PublishAsFile(XchangeFile xchangeFile, string endpoint, string exchange)
    {
        var receipt = await Publish(new PublishRequest
        {
            Endpoint = endpoint,
            Exchange = exchange,
            RoutingKey = _context?.ValueOf("RoutingKey"),
            ContentType = _context?.ValueOf("ContentType"),

            // The exchange id, so a redelivery is recognisable as the same message on the far side.
            // Publishers that set nothing leave the consumer no way to deduplicate, which is the
            // complaint this adapter logs when it receives one.
            MessageId = _context?.ValueOf("xchangeid"),

            Body = xchangeFile?.Data ?? ""
        });

        // The broker's receipt as the response, so what was sent and under which id is on the
        // exchange rather than only in a log.
        return new XchangeFile(
            JsonConvert.SerializeObject(receipt), xchangeFile?.Filename);
    }

    /// <summary>The control the UI needs before a data source is saved. Staged, so a failure names the step.</summary>
    public async Task<object> TestConnection()
    {
        var steps = new List<object>();
        IConnection probe = null;
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.Host, Port = _options.Port,
                UserName = _options.UserName, Password = _options.Password ?? "",
                VirtualHost = _options.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
            };
            if (_options.UseSsl) factory.Ssl = new SslOption { Enabled = true, ServerName = _options.Host };

            probe = await factory.CreateConnectionAsync("bitween-probe");
            steps.Add(new { step = "connect", ok = true, detail = probe.Endpoint.ToString() });

            await using var channel = await probe.CreateChannelAsync();
            steps.Add(new { step = "authenticate", ok = true, detail = _options.VirtualHost });

            foreach (var endpoint in _endpoints)
            {
                try
                {
                    var declared = await channel.QueueDeclarePassiveAsync(endpoint);
                    steps.Add(new { step = $"queue:{endpoint}", ok = true, detail = $"{declared.MessageCount} message(s)" });
                }
                catch (Exception ex)
                {
                    steps.Add(new { step = $"queue:{endpoint}", ok = false, detail = ex.Message });
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
        finally
        {
            try { if (probe != null) await probe.CloseAsync(); probe?.Dispose(); } catch { }
        }
    }

    /// <summary>What is actually on the broker, for the "pick a queue" step in the UI.</summary>
    public async Task<object> Discover()
    {
        var endpoints = new List<object>();
        foreach (var e in _endpoints)
            endpoints.Add(new { name = e, messages = await DepthAsync(e), consuming = _consumerTags.ContainsKey(e) });

        return new
        {
            host = $"{_options.Host}:{_options.Port}",
            virtualHost = _options.VirtualHost,
            endpoints,
            note = "AMQP alone can only report on queues we were told about. " +
                   "Listing everything on the broker needs the management plugin."
        };
    }

    public Task<object> GetStats() => Task.FromResult<object>(new
    {
        received = _received, acked = _acked, nacked = _nacked, failed = _failed, published = _published,
        poisoned = _poisoned,
        endpoints = _endpoints, prefetch = _options.Prefetch
    });

    public class PublishRequest
    {
        public string Endpoint { get; set; }
        public string Exchange { get; set; }
        public string RoutingKey { get; set; }
        public string MessageId { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
    }
}
