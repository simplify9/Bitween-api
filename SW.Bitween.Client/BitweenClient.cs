using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SW.Bitween.Client;

/// <summary>
/// One Bitween's API, as a service account. The account's token is traded for a session on the first
/// call, and traded again whenever the session runs out or is refused, so the token is all a caller
/// hands over. Keep one for the life of the program: it can be used from many threads at once, and
/// they share one session. What the account may do is what its roles allow: <c>exchanges.view</c> to
/// read, and <c>exchanges.operate</c> to retry.
/// </summary>
/// <example>
/// <code>
/// using var bitween = new BitweenClient("https://bitween.example.com", "bwt_...");
/// var failed = await bitween.Exchanges.SearchAsync(new ExchangeQuery { Status = ExchangeStatus.Failed });
/// </code>
/// </example>
public sealed class BitweenClient : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    readonly HttpClient http;
    readonly bool ownsHttp;
    readonly string token;
    readonly Lock signInLock = new();
    Task<Session>? signingIn;
    volatile Session? session;

    /// <param name="url">The Bitween's address, such as <c>https://bitween.example.com</c>.</param>
    /// <param name="token">A service account's token, starting <c>bwt_</c>.</param>
    public BitweenClient(string url, string token)
        : this(new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) }, token)
    {
        ownsHttp = true;
    }

    /// <summary>
    /// With an <see cref="HttpClient"/> of the caller's, whose <see cref="HttpClient.BaseAddress"/> is the
    /// Bitween's address, ending in <c>/</c> when it has a path. Disposing the client leaves it open.
    /// </summary>
    public BitweenClient(HttpClient http, string token)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.BaseAddress == null) throw new ArgumentException("The HttpClient needs a BaseAddress: the Bitween's address.", nameof(http));
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        this.http = http;
        this.token = token.Trim();
        Exchanges = new Exchanges(this);
        ScheduledRetries = new ScheduledRetries(this);
    }

    /// <summary>Search exchanges, read their files, and retry or export them.</summary>
    public Exchanges Exchanges { get; }

    /// <summary>The automatic retries waiting to run, and running one now.</summary>
    public ScheduledRetries ScheduledRetries { get; }

    internal async Task<T> GetAsync<T>(string path, CancellationToken cancellation)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path),
            HttpCompletionOption.ResponseContentRead, cancellation);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellation))!;
    }

    internal async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellation)
    {
        using var response = await SendAsync(() => Post(path, body), HttpCompletionOption.ResponseContentRead, cancellation);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellation))!;
    }

    internal async Task PostAsync(string path, object body, CancellationToken cancellation)
    {
        using var _ = await SendAsync(() => Post(path, body), HttpCompletionOption.ResponseContentRead, cancellation);
    }

    /// <summary>The answer's body as it comes, for a file too big to hold: the caller disposes it.</summary>
    internal async Task<Stream> PostForStreamAsync(string path, object body, CancellationToken cancellation)
    {
        var response = await SendAsync(() => Post(path, body), HttpCompletionOption.ResponseHeadersRead, cancellation);
        return await response.Content.ReadAsStreamAsync(cancellation);
    }

    static HttpRequestMessage Post(string path, object body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType(), options: Json) };

    async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, HttpCompletionOption completion,
        CancellationToken cancellation)
    {
        var used = await SessionAsync(null, cancellation);
        var response = await SendOnceAsync(make(), used, completion, cancellation);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Ended before its time, as by the account's roles changing: sign in again once and try again.
            response.Dispose();
            used = await SessionAsync(used, cancellation);
            response = await SendOnceAsync(make(), used, completion, cancellation);
        }

        if (response.IsSuccessStatusCode) return response;
        using (response) throw await BitweenClientException.FromAsync(response, cancellation);
    }

    async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, Session with,
        HttpCompletionOption completion, CancellationToken cancellation)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", with.Jwt);
            return await http.SendAsync(request, completion, cancellation);
        }
    }

    /// <summary>
    /// The session to send: the one held, unless it is about to end or is <paramref name="refused"/>,
    /// when the token is traded for a new one. Calls that find it so together share one trade and its
    /// answer, a refusal included: a bad token sent once a call would soon lock the address out.
    /// </summary>
    async Task<Session> SessionAsync(Session? refused, CancellationToken cancellation)
    {
        var held = session;
        if (Usable(held, refused)) return held!;

        Task<Session> signIn;
        lock (signInLock)
        {
            held = session;
            if (Usable(held, refused)) return held!;
            // Started apart from this lock, so it can't finish, and clear itself, before it is kept.
            signIn = signingIn ??= Task.Run(SignInAsync);
        }
        return await signIn.WaitAsync(cancellation);
    }

    async Task<Session> SignInAsync()
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/accounts/servicetoken", new { token }, Json);
            if (!response.IsSuccessStatusCode) throw await BitweenClientException.FromAsync(response, CancellationToken.None);
            var answer = await response.Content.ReadFromJsonAsync<SignIn>(Json);
            if (string.IsNullOrEmpty(answer?.Jwt))
                throw new BitweenClientException("Bitween answered the sign-in without a session; is this a Bitween?", response.StatusCode);

            // Renewed a minute early, so no call goes out on a session that ends on its way; halfway
            // through one shorter than two minutes, so it is still used.
            var early = Math.Min(60, answer.ExpiresIn / 2);
            return session = new Session(answer.Jwt, DateTimeOffset.UtcNow.AddSeconds(answer.ExpiresIn - early));
        }
        finally
        {
            lock (signInLock) signingIn = null;
        }
    }

    static bool Usable(Session? held, Session? refused) =>
        held != null && held != refused && DateTimeOffset.UtcNow < held.RenewAfter;

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
    }

    sealed record Session(string Jwt, DateTimeOffset RenewAfter);

    sealed record SignIn(string Jwt, int ExpiresIn);
}
