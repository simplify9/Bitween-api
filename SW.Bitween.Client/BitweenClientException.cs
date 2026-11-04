using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SW.Bitween.Client;

/// <summary>What a Bitween refused, and why.</summary>
public class BitweenClientException(string message, HttpStatusCode status, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>
    /// Bitween's reasons by code, such as <c>ALREADY_RETRIED</c> or <c>DATABASE_TIMED_OUT</c>, each with
    /// its messages. Empty when it gave none.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    /// <summary>The first of <see cref="Errors"/>' codes, or null when there are none.</summary>
    public string? Code => Errors.Keys.FirstOrDefault();

    internal static async Task<BitweenClientException> FromAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var body = await response.Content.ReadAsStringAsync(cancellation);
        var errors = ErrorsIn(body);
        var messages = errors.SelectMany(e => e.Value).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        var message = messages.Count > 0 ? string.Join(" ", messages) : response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Bitween refused the service token, or the session made with it.",
            HttpStatusCode.Forbidden => "The service account's roles don't have the permission this needs.",
            HttpStatusCode.NotFound => "Bitween has no such thing.",
            _ => $"Bitween answered {(int)response.StatusCode}{(string.IsNullOrWhiteSpace(body) ? "" : ": " + body.Trim())}",
        };
        return new BitweenClientException(message, response.StatusCode, errors);
    }

    /// <summary>
    /// Validation answers as <c>{ CODE: [messages] }</c>, and problem details with <c>errors</c> in that
    /// shape. Problem details without them say only the status, so they give no reasons.
    /// </summary>
    static Dictionary<string, string[]> ErrorsIn(string body)
    {
        var errors = new Dictionary<string, string[]>();
        try
        {
            if (JsonNode.Parse(body) is not JsonObject answer) return errors;
            var reasons = answer["errors"] as JsonObject ?? (answer.ContainsKey("status") ? new JsonObject() : answer);
            foreach (var (code, value) in reasons)
                errors[code] = value switch
                {
                    JsonArray list => list.Select(m => m?.ToString() ?? "").ToArray(),
                    null => [],
                    _ => [value.ToString()],
                };
        }
        catch (JsonException) { }
        return errors;
    }
}
