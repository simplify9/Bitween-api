using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// The "Login" auth type, shared by the HTTP handler and receiver: post credentials to a login URL
/// and read a bearer token out of the reply. APIs differ in both the body they expect and where
/// they put the token, so each can be set per adapter; left blank, the old fixed shapes apply.
/// </summary>
/// <remarks>
/// The token is reused until shortly before it expires, as <see cref="OAuthTokens"/> does for
/// OAuth2. Signing in for every message turned a burst of deliveries into a burst of logins, which
/// providers throttle: one allowing 30 token requests a minute capped the whole flow at 30
/// messages a minute, for a token good for an hour. The lifetime is the reply's <c>expires_in</c>,
/// else the token's own <c>exp</c> when it is a JWT, else five minutes.
/// </remarks>
internal static class HttpLogin
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset UseUntil)> Cache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    public static async Task<string> GetToken(HttpClient client, string loginUrl, string? loginBody,
        string? tokenPath, string? username, string? password, object defaultBody) =>
        (await GetTokenReused(client, loginUrl, loginBody, tokenPath, username, password, defaultBody)).Token;

    /// <summary>
    /// The token, and whether it was reused rather than fetched now - a reused token the partner
    /// refuses is worth one more sign-in, a fresh one is not.
    /// </summary>
    public static async Task<(string Token, bool FromCache)> GetTokenReused(HttpClient client, string loginUrl,
        string? loginBody, string? tokenPath, string? username, string? password, object defaultBody)
    {
        var body = string.IsNullOrWhiteSpace(loginBody)
            ? JsonConvert.SerializeObject(defaultBody)
            : RenderBody(loginBody, username, password);

        // The body holds the credentials, so two subscriptions on different accounts never share a
        // token. Only the hash is kept.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loginUrl + "\n" + tokenPath + "\n" + body)));
        if (TryCached(key, out var cached))
            return (cached, true);

        // One sign-in per key at a time: messages arriving together after expiry share it.
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (TryCached(key, out cached))
                return (cached, true);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await client.PostAsync(new Uri(loginUrl),
                new StringContent(body, Encoding.UTF8, "application/json"), timeout.Token);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new SWException(
                    $"Login to {loginUrl} failed with {(int)response.StatusCode} {response.StatusCode}: {responseBody}");

            var token = ReadToken(responseBody, tokenPath);
            var now = DateTimeOffset.UtcNow;
            var lifetime = Lifetime(responseBody, token, now);
            if (lifetime > TimeSpan.Zero)
                Cache[key] = (token, now + (lifetime > RenewBefore * 2 ? lifetime - RenewBefore : lifetime / 2));
            return (token, false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops a token the partner refused, so the next message signs in again.</summary>
    public static void Forget(string token)
    {
        foreach (var entry in Cache)
            if (entry.Value.Token == token)
                Cache.TryRemove(entry);
    }

    /// <summary>For tests: forget every cached token.</summary>
    internal static void Clear() => Cache.Clear();

    private static bool TryCached(string key, out string token)
    {
        if (Cache.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow < entry.UseUntil)
        {
            token = entry.Token;
            return true;
        }
        token = string.Empty;
        return false;
    }

    /// <summary>
    /// How long the token is good for: the reply's <c>expires_in</c> (seconds, number or string),
    /// else the JWT's <c>exp</c>, else five minutes. Zero or less means don't keep it.
    /// </summary>
    internal static TimeSpan Lifetime(string responseBody, string token, DateTimeOffset now)
    {
        try
        {
            if (JToken.Parse(responseBody) is JObject reply
                && reply.GetValue("expires_in", StringComparison.OrdinalIgnoreCase) is JValue expiresIn
                && double.TryParse(expiresIn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        catch (JsonReaderException)
        {
            // ReadToken already accepted the reply; a reply it can read is JSON.
        }

        var parts = token.Split('.');
        if (parts.Length == 3)
        {
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                if (claims["exp"] is JValue { Type: JTokenType.Integer or JTokenType.Float } exp)
                    return DateTimeOffset.FromUnixTimeSeconds((long)exp) - now;
            }
            catch (Exception e) when (e is FormatException or JsonReaderException)
            {
                // Three parts but not a JWT after all.
            }
        }

        return DefaultLifetime;
    }

    internal static string RenderBody(string template, string? username, string? password) =>
        Template.Parse(template).Render(Hash.FromDictionary(new Dictionary<string, object>
        {
            ["username"] = JsonEscape(username),
            ["password"] = JsonEscape(password)
        }));

    // The template is JSON, so a quote or backslash in a credential would otherwise break the body.
    private static string JsonEscape(string? value) => JsonConvert.ToString(value ?? string.Empty)[1..^1];

    internal static string ReadToken(string responseBody, string? tokenPath)
    {
        JToken json;
        try
        {
            json = JToken.Parse(responseBody);
        }
        catch (JsonReaderException)
        {
            throw new SWException($"The login response is not JSON: {responseBody}");
        }

        if (string.IsNullOrWhiteSpace(tokenPath))
        {
            // Matched case-insensitively, as the old fixed deserialisation did.
            var jwt = json is JObject obj
                && obj.GetValue("Jwt", StringComparison.OrdinalIgnoreCase) is JValue { Type: JTokenType.String } value
                ? (string?)value
                : null;
            return !string.IsNullOrEmpty(jwt)
                ? jwt
                : throw new SWException(
                    "The login response has no 'jwt' field. Set LoginTokenPath to where the token is.");
        }

        var path = tokenPath.Trim();
        return json.SelectToken(path) is JValue { Type: JTokenType.String } token && !string.IsNullOrEmpty((string?)token)
            ? (string)token!
            : throw new SWException($"The login response has no token at '{path}'.");
    }
}
