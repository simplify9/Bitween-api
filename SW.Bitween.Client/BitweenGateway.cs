using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SW.Bitween.Client;

/// <summary>How a gateway call ended, as far as the answer says.</summary>
public enum GatewayOutcome
{
    /// <summary>Delivered. <see cref="GatewayAnswer.Body"/> is the destination's response, if it gave one.</summary>
    Delivered,

    /// <summary>Delivered, but the response was marked bad. <see cref="GatewayAnswer.Body"/> is that response.</summary>
    BadResponse,

    /// <summary>Failed, and won't be retried. <see cref="GatewayAnswer.Error"/> says why when the gateway tells.</summary>
    Failed,

    /// <summary>
    /// Failed, but Bitween has the message and will deliver it again by
    /// <see cref="GatewayAnswer.NextAttemptOn"/>: sending it again would make a second one.
    /// </summary>
    Retrying,

    /// <summary>
    /// The destination has failed past its retry budget; Bitween keeps the message and tries it again once
    /// it recovers. Sending it again would make a second one.
    /// </summary>
    Held,

    /// <summary>The wait ended first. The exchange carries on: follow it by <see cref="GatewayAnswer.ExchangeId"/>.</summary>
    StillRunning,
}

/// <summary>An async call's answer: the exchange it made, or the earlier one it was answered with.</summary>
public sealed class GatewayAccepted
{
    public string ExchangeId { get; init; } = null!;

    /// <summary>The same <c>Idempotency-Key</c> was sent before, and this is that call's exchange.</summary>
    public bool Replayed { get; init; }

    /// <summary>The payload repeats one its information type drops as a duplicate: the exchange it repeats.</summary>
    public string? DuplicateOf { get; init; }
}

/// <summary>A sync call's answer.</summary>
public sealed class GatewayAnswer
{
    public GatewayOutcome Outcome { get; init; }

    /// <summary>The HTTP status the gateway answered with.</summary>
    public HttpStatusCode Status { get; init; }

    /// <summary>The exchange, when the gateway named it: every answer does from a Bitween that sends the header.</summary>
    public string? ExchangeId { get; init; }

    /// <summary>The destination's response, for <see cref="GatewayOutcome.Delivered"/> and <see cref="GatewayOutcome.BadResponse"/>.</summary>
    public string? Body { get; init; }

    public string? ContentType { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset? NextAttemptOn { get; init; }

    /// <summary>The same <c>Idempotency-Key</c> was sent before, and this answers that call's exchange.</summary>
    public bool Replayed { get; init; }

    /// <summary>The payload repeats one its information type drops as a duplicate: the exchange it repeats.</summary>
    public string? DuplicateOf { get; init; }
}

/// <summary>
/// Sends to a Bitween's API gateways as a partner, with one of the partner's keys: what a partner's
/// own program does, where <see cref="BitweenClient"/> is a service account managing the Bitween.
/// Refusals - an unknown gateway, a key it doesn't take, a gateway switched off, a payload its
/// information type's schema rejects - throw <see cref="BitweenClientException"/>; how a delivery
/// went comes back as a <see cref="GatewayAnswer"/>.
/// </summary>
/// <example>
/// <code>
/// using var gateway = new BitweenGateway("https://bitween.example.com", partnerKey);
/// var accepted = await gateway.SendAsync("orders", json, idempotencyKey: order.Number);
/// </code>
/// </example>
public sealed class BitweenGateway : IDisposable
{
    /// <summary>The header Bitween names a call's exchange in.</summary>
    public const string ExchangeIdHeader = "Bitween-Exchange-Id";

    const string DuplicateOfHeader = "Bitween-Duplicate-Of";
    const string ReplayHeader = "Idempotent-Replay";

    readonly HttpClient http;
    readonly bool ownsHttp;
    readonly string key;
    readonly string keyHeader;

    /// <param name="url">The Bitween's address, such as <c>https://bitween.example.com</c>.</param>
    /// <param name="partnerKey">One of the partner's keys.</param>
    /// <param name="keyHeader">The header the gateway reads the key from; <c>partnerkey</c> always works.</param>
    public BitweenGateway(string url, string partnerKey, string keyHeader = "partnerkey")
        : this(new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(6) }, partnerKey, keyHeader)
    {
        ownsHttp = true;
    }

    /// <summary>
    /// With an <see cref="HttpClient"/> of the caller's, whose <see cref="HttpClient.BaseAddress"/> is the
    /// Bitween's address, ending in <c>/</c> when it has a path. Its timeout must outlast the longest sync
    /// wait asked for. Disposing this leaves it open.
    /// </summary>
    public BitweenGateway(HttpClient http, string partnerKey, string keyHeader = "partnerkey")
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.BaseAddress == null) throw new ArgumentException("The HttpClient needs a BaseAddress: the Bitween's address.", nameof(http));
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHeader);
        this.http = http;
        key = partnerKey.Trim();
        this.keyHeader = keyHeader.Trim();
    }

    /// <summary>
    /// Sends <paramref name="payload"/> to the gateway and returns as soon as Bitween has stored it.
    /// With <paramref name="idempotencyKey"/>, a repeat of a call that may not have arrived is answered
    /// with the first one's exchange rather than making a second.
    /// </summary>
    public async Task<GatewayAccepted> SendAsync(string urlName, string payload, string? idempotencyKey = null,
        string contentType = "application/json", CancellationToken cancellation = default)
    {
        using var response = await PostAsync(urlName, "async", payload, idempotencyKey, contentType, null, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted && response.StatusCode != HttpStatusCode.OK)
            throw await RefusalAsync(response, body, cancellation);
        return new GatewayAccepted
        {
            ExchangeId = Header(response, ExchangeIdHeader) ?? BareId(body)
                ?? throw new BitweenClientException("Bitween accepted the call without naming its exchange.", response.StatusCode),
            Replayed = Header(response, ReplayHeader) == "true",
            DuplicateOf = Header(response, DuplicateOfHeader),
        };
    }

    /// <summary>
    /// Sends <paramref name="payload"/> and waits for the delivery's result, at most
    /// <paramref name="waitSeconds"/> (Bitween's default when null, 120; never more than its limit).
    /// </summary>
    public async Task<GatewayAnswer> SendAndWaitAsync(string urlName, string payload, int? waitSeconds = null,
        string? idempotencyKey = null, string contentType = "application/json", CancellationToken cancellation = default)
    {
        if (waitSeconds is <= 0) throw new ArgumentOutOfRangeException(nameof(waitSeconds), "A wait is at least a second.");
        using var response = await PostAsync(urlName, "sync", payload, idempotencyKey, contentType, waitSeconds, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        var exchangeId = Header(response, ExchangeIdHeader);
        var type = response.Content.Headers.ContentType?.MediaType;

        GatewayAnswer Answer(GatewayOutcome outcome, string? answerBody = null, string? error = null,
            DateTimeOffset? next = null, string? id = null) => new()
        {
            Outcome = outcome,
            Status = response.StatusCode,
            ExchangeId = exchangeId ?? id,
            Body = answerBody,
            ContentType = answerBody == null ? null : type,
            Error = error,
            NextAttemptOn = next,
            Replayed = Header(response, ReplayHeader) == "true",
            DuplicateOf = Header(response, DuplicateOfHeader),
        };

        switch ((int)response.StatusCode)
        {
            case 200:
                // Delivered with no response: answered with the exchange id alone. Only knowable when the
                // header names it; an older Bitween's id reads as the body.
                return exchangeId != null && BareId(body) == exchangeId
                    ? Answer(GatewayOutcome.Delivered)
                    : Answer(GatewayOutcome.Delivered, body);
            case 202:
                var pending = Failure(body);
                return pending?.Status switch
                {
                    "retrying" => Answer(GatewayOutcome.Retrying, error: pending.Error, next: pending.NextAttemptOn, id: pending.ExchangeId),
                    "held" => Answer(GatewayOutcome.Held, error: pending.Error, id: pending.ExchangeId),
                    _ => Answer(GatewayOutcome.StillRunning, id: BareId(body)),
                };
            case 400 when SchemaMismatch(body) is { } mismatch:
                throw mismatch;
            case 400:
                // A Legacy gateway answers every failure with an empty 400, as a response marked bad
                // with nothing in it would be: told apart only by the gateway's setting.
                return string.IsNullOrEmpty(body) ? Answer(GatewayOutcome.Failed) : Answer(GatewayOutcome.BadResponse, body);
            case 422:
            case 502:
                var failed = Failure(body);
                return failed?.Status == "failed"
                    ? Answer(GatewayOutcome.Failed, error: failed.Error, id: failed.ExchangeId)
                    : throw await RefusalAsync(response, body, cancellation);
            default:
                throw await RefusalAsync(response, body, cancellation);
        }
    }

    async Task<HttpResponseMessage> PostAsync(string urlName, string mode, string payload, string? idempotencyKey,
        string contentType, int? waitSeconds, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlName);
        ArgumentNullException.ThrowIfNull(payload);
        var path = string.Join('/', urlName.Trim('/').Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/gateway/{path}/{mode}")
        {
            Content = new StringContent(payload, Encoding.UTF8, contentType),
        };
        request.Headers.TryAddWithoutValidation(keyHeader, key);
        if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.Add("Idempotency-Key", idempotencyKey.Trim());
        if (waitSeconds != null) request.Headers.Add("Wait-Period", waitSeconds.Value.ToString(CultureInfo.InvariantCulture));
        return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation);
    }

    static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// The body as an exchange id, which the gateway answers as plain text (a JSON string from some
    /// hosts); null when it is anything else, such as an object.
    /// </summary>
    static string? BareId(string body)
    {
        var text = body.Trim();
        if (text.Length == 0 || text.Length > 64) return null;
        if (text.StartsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(text);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') ? text : null;
    }

    sealed record FailureBody(string? ExchangeId, string? Status, string? Error, DateTimeOffset? NextAttemptOn);

    static FailureBody? Failure(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body).ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<FailureBody>(body, BitweenClient.Json)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary><c>{ error: "SCHEMA_MISMATCH", errors: [...] }</c>: the payload doesn't fit its information type.</summary>
    static BitweenClientException? SchemaMismatch(string body)
    {
        try
        {
            var answer = JsonSerializer.Deserialize<JsonElement>(body);
            if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.String || error.GetString() != "SCHEMA_MISMATCH")
                return null;
            var messages = answer.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(e => e.ToString()).ToArray()
                : [];
            return new BitweenClientException(
                messages.Length > 0 ? string.Join(" ", messages) : "The payload doesn't fit its information type's schema.",
                HttpStatusCode.BadRequest, new Dictionary<string, string[]> { ["SCHEMA_MISMATCH"] = messages });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static async Task<BitweenClientException> RefusalAsync(HttpResponseMessage response, string body, CancellationToken cancellation)
    {
        if (SchemaMismatch(body) is { } mismatch) return mismatch;
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => new BitweenClientException(
                "Bitween has no such gateway, or the subscription it runs is switched off.", response.StatusCode),
            HttpStatusCode.Unauthorized => new BitweenClientException(
                "The gateway didn't take the key, or the partner isn't attached to it.", response.StatusCode),
            _ => await BitweenClientException.FromAsync(response, cancellation),
        };
    }

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
    }
}
