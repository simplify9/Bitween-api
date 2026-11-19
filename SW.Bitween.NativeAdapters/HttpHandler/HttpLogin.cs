using System.Collections.Concurrent;
using System.Globalization;
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
/// messages a minute, for a token good for an hour. The lifetime is read from the reply
/// (<c>LoginExpiresInPath</c>, <c>expires_in</c> unless set), else the token's own <c>exp</c> when it
/// is a JWT, else <c>LoginTokenLifetimeSeconds</c> (five minutes). How the sign-in is sent
/// (<c>LoginContentType</c>) and where the token goes (<c>LoginTokenHeader</c>) can be set too, so
/// standard token endpoints and session logins alike fit without code.
/// </remarks>
internal static class HttpLogin
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset UseUntil)> Cache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);
    internal const int DefaultLifetimeSeconds = 300;
    internal const string DefaultExpiresInPath = "expires_in";

    public static async Task<string> GetToken(HttpClient client, string loginUrl, string? loginBody,
        string? tokenPath, string? username, string? password, object defaultBody) =>
        (await GetTokenReused(client, new LoginOptions(loginUrl, defaultBody)
        {
            Body = loginBody, TokenPath = tokenPath, Username = username, Password = password
        })).Token;

    /// <summary>
    /// The token, and whether it was reused rather than fetched now - a reused token the partner
    /// refuses is worth one more sign-in, a fresh one is not.
    /// </summary>
    public static async Task<(string Token, bool FromCache)> GetTokenReused(HttpClient client, LoginOptions options)
    {
        var body = string.IsNullOrWhiteSpace(options.Body)
            ? JsonConvert.SerializeObject(options.DefaultBody)
            : RenderBody(options.Body, options.Username, options.Password);
        var form = IsForm(options.ContentType);
        var reuse = options.LifetimeSeconds is not 0;

        // The body holds the credentials, so two subscriptions on different accounts never share a
        // token. Only the hash is kept.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", options.Url, form, options.TokenPath, body))));
        if (reuse && TryCached(key, out var cached))
            return (cached, true);

        // One sign-in per key at a time: messages arriving together after expiry share it.
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (reuse && TryCached(key, out cached))
                return (cached, true);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await client.PostAsync(new Uri(options.Url), Content(body, form), timeout.Token);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new SWException(
                    $"Login to {options.Url} failed with {(int)response.StatusCode} {response.StatusCode}: {responseBody}");

            var token = ReadToken(responseBody, options.TokenPath);
            if (reuse)
            {
                var now = DateTimeOffset.UtcNow;
                var lifetime = Lifetime(responseBody, token, now, options.ExpiresInPath, options.LifetimeSeconds);
                if (lifetime > TimeSpan.Zero)
                    Cache[key] = (token, now + (lifetime > RenewBefore * 2 ? lifetime - RenewBefore : lifetime / 2));
            }
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

    /// <summary>
    /// The header the token goes in. Empty is <c>Authorization: Bearer {token}</c>; otherwise a
    /// <c>Name: value</c> template with <c>{{token}}</c> in the value, e.g. <c>X-Auth-Token: {{token}}</c>
    /// or <c>Authorization: Token {{token}}</c>.
    /// </summary>
    public static (string Name, string Value) Header(string? template, string token)
    {
        if (string.IsNullOrWhiteSpace(template))
            return ("Authorization", "Bearer " + token);

        var colon = template.IndexOf(':');
        var name = colon > 0 ? template[..colon].Trim() : string.Empty;
        var value = colon > 0 ? template[(colon + 1)..].Trim() : string.Empty;
        if (name.Length == 0 || !value.Contains("{{token}}"))
            throw new SWException(
                $"LoginTokenHeader '{template}' should read 'Name: value' with {{{{token}}}} in the value, e.g. 'X-Auth-Token: {{{{token}}}}'.");
        return (name, value.Replace("{{token}}", token));
    }

    /// <summary>Puts the token's header on a request: Authorization as such, any other by name.</summary>
    public static void Apply(HttpRequestMessage request, (string Name, string Value) header)
    {
        if (header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(header.Value);
        else
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
    }

    private static bool IsForm(string? contentType) =>
        string.Equals(contentType?.Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static HttpContent Content(string body, bool form)
    {
        if (!form)
            return new StringContent(body, Encoding.UTF8, "application/json");

        // The template is written as JSON either way, so credentials are escaped the same; a form
        // login sends its fields form-encoded.
        Dictionary<string, string?> fields;
        try
        {
            fields = JObject.Parse(body).Properties()
                .ToDictionary(p => p.Name, p => p.Value.Type == JTokenType.Null ? null : p.Value.ToString());
        }
        catch (JsonReaderException)
        {
            throw new SWException("LoginBody must be a flat JSON object to be sent form-encoded.");
        }
        return new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value ?? "")));
    }

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
    /// How long the token is good for. The reply's value at <paramref name="expiresInPath"/>
    /// (<c>expires_in</c> unless set): seconds, as a number or a string, or a date and time it
    /// expires at. Else the JWT's <c>exp</c>. Else <paramref name="fallbackSeconds"/>, five minutes
    /// unless set. Zero or less means don't keep it.
    /// </summary>
    internal static TimeSpan Lifetime(string responseBody, string token, DateTimeOffset now,
        string? expiresInPath = null, int? fallbackSeconds = null)
    {
        var path = string.IsNullOrWhiteSpace(expiresInPath) ? DefaultExpiresInPath : expiresInPath.Trim();
        try
        {
            var reply = JsonConvert.DeserializeObject<JToken>(responseBody,
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
            if (reply?.SelectToken(path) is JValue value && value.Value is not null)
            {
                var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture)!;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    return TimeSpan.FromSeconds(seconds);
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
                    return at - now;
            }
        }
        catch (JsonException)
        {
            // ReadToken already accepted the reply, so it is JSON; a path that doesn't parse reads nothing.
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

        return TimeSpan.FromSeconds(fallbackSeconds ?? DefaultLifetimeSeconds);
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

/// <summary>How a <c>Login</c> sign-in is made, read and kept. Shared by the HTTP handler and receiver.</summary>
internal sealed record LoginOptions(string Url, object DefaultBody)
{
    public string? Body { get; init; }
    public string? ContentType { get; init; }
    public string? TokenPath { get; init; }
    public string? ExpiresInPath { get; init; }
    public int? LifetimeSeconds { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}
