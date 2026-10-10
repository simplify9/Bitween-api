using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SW.Bitween.Cli;

/// <summary>A Bitween's API, refused or answered.</summary>
public class BitweenApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Calls to one Bitween's API as the signed-in member: the access token goes with each call, and is
/// renewed with the refresh token when it has expired, the way the browser renews it — the renewed
/// tokens are saved back to the profile.
/// </summary>
public class BitweenApi : IDisposable
{
    readonly HttpClient http;
    readonly Profiles profiles;
    readonly string profileName;
    Profile profile;

    public BitweenApi(Profiles profiles, string profileName, Profile profile, HttpMessageHandler handler = null)
    {
        this.profiles = profiles;
        this.profileName = profileName;
        this.profile = profile;
        http = new HttpClient(handler ?? Handler(profile.Insecure))
        {
            BaseAddress = new Uri(profile.Url.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(10),
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("bitween-cli", typeof(BitweenApi).Assembly.GetName().Version?.ToString() ?? "0"));
    }

    public string Url => profile.Url;

    /// <summary>
    /// Cookies are handled by hand, so the refresh token is only ever where the profile keeps it.
    /// Insecure skips certificate checks, for a Bitween on a self-signed development certificate.
    /// </summary>
    /// <summary>For tests: the handler every call goes through, an in-process Bitween's, say.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride;

    static HttpMessageHandler Handler(bool insecure)
    {
        if (HandlerOverride != null) return HandlerOverride();
        var handler = new HttpClientHandler { UseCookies = false };
        if (insecure)
        {
            Console.Error.WriteLine("Warning: not checking the Bitween's certificate (--insecure).");
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }

    /// <summary>Signs in with an email and password, and returns the profile to keep.</summary>
    public static async Task<Profile> SignInAsync(string url, string email, string password, bool insecure = false, HttpMessageHandler handler = null)
    {
        using var http = new HttpClient(handler ?? Handler(insecure)) { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        var response = await http.PostAsJsonAsync("api/accounts/login", new { Username = email, Password = password });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new BitweenApiException(Refusal(response.StatusCode, body), response.StatusCode);

        var answer = JsonNode.Parse(body);
        if ((bool?)answer?["mustChangePassword"] == true)
            throw new BitweenApiException("This account must change its password first: sign in to Bitween in a browser, change it, then sign in here.");
        return new Profile
        {
            Url = url.TrimEnd('/'),
            Email = email,
            AccessToken = (string)answer?["jwt"],
            RefreshToken = RefreshTokenIn(response) ?? throw new BitweenApiException("Bitween signed in but sent no refresh token; is this a Bitween?"),
            Insecure = insecure,
        };
    }

    /// <summary>Trades a code from the browser, with this process's verifier, for a profile to keep.</summary>
    public static async Task<Profile> RedeemAsync(string url, string code, string verifier, bool insecure = false, HttpMessageHandler handler = null)
    {
        using var http = new HttpClient(handler ?? Handler(insecure)) { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        var response = await http.PostAsJsonAsync("api/accounts/clitoken", new { Code = code, CodeVerifier = verifier });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new BitweenApiException(Refusal(response.StatusCode, body), response.StatusCode);

        var answer = JsonNode.Parse(body);
        return new Profile
        {
            Url = url.TrimEnd('/'),
            Email = (string)answer?["email"],
            AccessToken = (string)answer?["jwt"] ?? throw new BitweenApiException("Bitween answered without a session; is this a Bitween?"),
            RefreshToken = (string)answer?["refreshToken"],
            Insecure = insecure,
        };
    }

    /// <summary>
    /// Ends the session on the Bitween, so its refresh token stops working there and not only here.
    /// Best effort: a Bitween that can't be reached, or that predates this, just isn't told.
    /// </summary>
    public static async Task<bool> EndSessionAsync(Profile profile, HttpMessageHandler handler = null)
    {
        if (string.IsNullOrEmpty(profile.RefreshToken)) return false;
        try
        {
            using var http = new HttpClient(handler ?? Handler(profile.Insecure))
            {
                BaseAddress = new Uri(profile.Url.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15),
            };
            using var response = await http.PostAsJsonAsync("api/accounts/logout", new { RefreshToken = profile.RefreshToken });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<JsonNode> GetAsync(string path) => await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path));

    public async Task<JsonNode> PostAsync(string path, object body) =>
        await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) });

    public async Task<JsonNode> PostFileAsync(string path, string file, string contentType)
    {
        return await SendAsync(() =>
        {
            var content = new StreamContent(File.OpenRead(file));
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        });
    }

    async Task<JsonNode> SendAsync(Func<HttpRequestMessage> make)
    {
        if (Expired(profile.AccessToken)) await RenewAsync();
        var response = await SendOnceAsync(make());
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Expired between the check and the call, or revoked: renew once and try again.
            response.Dispose();
            await RenewAsync();
            response = await SendOnceAsync(make());
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new BitweenApiException(Refusal(response.StatusCode, text), response.StatusCode);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
    }

    Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.AccessToken);
        return http.SendAsync(request);
    }

    async Task RenewAsync()
    {
        if (string.IsNullOrEmpty(profile.RefreshToken))
            throw new BitweenApiException($"The session with {profile.Url} has ended; sign in again with bitween login {profile.Url}.");
        var response = await http.PostAsJsonAsync("api/accounts/login", new { RefreshToken = profile.RefreshToken });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new BitweenApiException($"The session with {profile.Url} has ended; sign in again with bitween login {profile.Url}.", response.StatusCode);

        profile.AccessToken = (string)JsonNode.Parse(body)?["jwt"];
        profile.RefreshToken = RefreshTokenIn(response) ?? profile.RefreshToken;
        profiles.Put(profileName, profile, makeCurrent: false);
    }

    static string RefreshTokenIn(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var first = cookie.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == "refresh_token") return Uri.UnescapeDataString(first[(eq + 1)..].Trim());
        }
        return null;
    }

    /// <summary>Whether a JWT has expired, or will within the minute; unreadable counts as expired.</summary>
    internal static bool Expired(string jwt)
    {
        try
        {
            var payload = jwt?.Split('.')[1];
            if (payload == null) return true;
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var exp = (long?)JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))?["exp"];
            return exp == null || DateTimeOffset.FromUnixTimeSeconds(exp.Value) < DateTimeOffset.UtcNow.AddMinutes(1);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException)
        {
            return true;
        }
    }

    /// <summary>What Bitween said, as a sentence: its validation messages, or the status.</summary>
    static string Refusal(HttpStatusCode status, string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject problems)
            {
                // Validation answers as { field: [messages] }, or problem details with "errors". Problem
                // details without them say only the status, which the sentences below say better.
                var errors = problems["errors"] as JsonObject ?? (problems.ContainsKey("status") ? new JsonObject() : problems);
                var messages = errors.SelectMany(e => e.Value is JsonArray a ? a.Select(x => (string)x) : [e.Value?.ToString()])
                    .Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                if (messages.Count > 0) return string.Join(" ", messages);
            }
        }
        catch (JsonException) { }
        return status switch
        {
            HttpStatusCode.Unauthorized => "Bitween refused the sign-in.",
            HttpStatusCode.Forbidden => "Your Bitween account doesn't have the permission this needs.",
            HttpStatusCode.NotFound => "Bitween has no such thing.",
            _ => $"Bitween answered {(int)status}{(string.IsNullOrWhiteSpace(body) ? "" : ": " + body.Trim())}",
        };
    }

    public void Dispose() => http.Dispose();
}
