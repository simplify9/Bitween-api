using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// OAuth2 client-credentials tokens for the HTTP adapters, fetched once and reused until shortly
/// before they expire.
/// </summary>
/// <remarks>
/// A token used to be fetched for every message, which turns a burst of deliveries into a burst of
/// token requests the provider may throttle. And a failed fetch was not checked: the request went
/// out with "Bearer null", and the partner's 401 was all anyone saw.
/// </remarks>
internal static class OAuthTokens
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> Cache = new();
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static async Task<string> GetAsync(HttpClient client, string? tokenUrl, string? clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(tokenUrl) || string.IsNullOrWhiteSpace(clientId))
            throw new SWException("OAuth2 needs LoginUrl (the token endpoint) and ClientId.");

        var key = tokenUrl + "|" + clientId + "|" +
                  Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret ?? "")));
        if (Cache.TryGetValue(key, out var cached) && cached.Expires - RenewBefore > DateTimeOffset.UtcNow)
            return cached.Token;

        using var timeout = new CancellationTokenSource(RequestTimeout);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret ?? "",
                ["grant_type"] = "client_credentials",
            })
        }, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);

        if (!response.IsSuccessStatusCode)
            throw new SWException($"The OAuth2 token endpoint answered {(int)response.StatusCode} {response.StatusCode}.");

        JObject json;
        try { json = JObject.Parse(body); }
        catch { throw new SWException("The OAuth2 token endpoint did not answer with JSON."); }

        var token = json["access_token"]?.ToString();
        if (string.IsNullOrEmpty(token))
            throw new SWException("The OAuth2 token endpoint did not return an access_token.");

        var lifetime = int.TryParse(json["expires_in"]?.ToString(), out var seconds) && seconds > 0 ? seconds : 300;
        Cache[key] = (token, DateTimeOffset.UtcNow.AddSeconds(lifetime));
        return token;
    }

    /// <summary>For tests: forget every cached token.</summary>
    internal static void Clear() => Cache.Clear();
}
