using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// What only a request through the real pipeline can show: which routes answer a caller with no
/// token, that a token from sign-in is honoured, that sign-in is rate limited per address, and that
/// the headers a browser relies on are sent.
/// </summary>
[Collection("Http")]
public class PipelineTests(HttpFixture fixture)
{
    /// <summary>
    /// The only handlers callable without a session. Each answers for itself: the client config is
    /// public by design, sign-in and sign-out are how a session starts and ends, and the two older
    /// partner endpoints authenticate the partner's key in the handler. A new entry here is a new
    /// door into the API, so the list is pinned rather than discovered.
    /// </summary>
    static readonly string[] UnprotectedHandlers =
    [
        // Redeems a signed one-time code with the CLI's PKCE verifier: no session to have yet.
        "SW.Bitween.Resources.Accounts.CliToken",
        "SW.Bitween.Resources.Accounts.Login",
        "SW.Bitween.Resources.Accounts.Logout",
        "SW.Bitween.Resources.Settings.Config",
        "SW.Bitween.Resources.Xchanges.Get",
        "SW.Bitween.Resources.Xchanges.Update",
    ];

    static IEnumerable<Type> HandlerTypes() =>
        typeof(BitweenDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.Namespace?.StartsWith("SW.Bitween.Resources.") == true &&
                        t.GetInterfaces().Any(i => i.Namespace == "SW.PrimitiveTypes" && i.Name.Contains("Handler")));

    [Fact]
    public void Only_the_known_handlers_are_open_without_a_session()
    {
        var open = HandlerTypes()
            .Where(t => t.GetCustomAttributes().Any(a => a.GetType().Name == "UnprotectAttribute"))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(UnprotectedHandlers.OrderBy(n => n), open);
    }

    public static IEnumerable<object[]> Resources() =>
        HandlerTypes().Select(t => t.Namespace!.Split('.').Last().ToLowerInvariant())
            .Distinct().OrderBy(r => r).Select(r => new object[] { r });

    /// <summary>
    /// Every resource refuses a caller with no token — its list, a record by key, a command — rather
    /// than answering, which is what a route that slipped out of protection would do.
    /// </summary>
    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Every_resource_refuses_a_caller_without_a_token(string resource)
    {
        using var client = fixture.Client();

        var calls = new List<(string Method, string Path, HttpResponseMessage Response)>
        {
            ("GET", $"/api/{resource}", await client.GetAsync($"/api/{resource}")),
            ("POST", $"/api/{resource}", await client.PostAsync($"/api/{resource}", Json()))
        };
        // A record by key, except the one partner endpoint that reads a key and authenticates the
        // partner itself (Xchanges.Get, pinned above).
        if (resource != "xchanges")
            calls.Add(("GET", $"/api/{resource}/1", await client.GetAsync($"/api/{resource}/1")));

        foreach (var (method, path, response) in calls)
            Assert.False(response.IsSuccessStatusCode,
                $"{method} {path} answered {(int)response.StatusCode} to a caller with no token");
    }

    public static IEnumerable<object[]> NamedHandlers() =>
        HandlerTypes()
            .Where(t => t.GetCustomAttributes().All(a => a.GetType().Name != "UnprotectAttribute"))
            .Select(t => (Type: t, Name: (string)t.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "HandlerNameAttribute")?.GetType()
                .GetProperty("Name")?.GetValue(t.GetCustomAttributes().First(a => a.GetType().Name == "HandlerNameAttribute"))!))
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .Select(h => new object[] { h.Type.Namespace!.Split('.').Last().ToLowerInvariant(), h.Name.ToLowerInvariant(), h.Type.Name })
            .OrderBy(o => o[0]).ThenBy(o => o[1]);

    /// <summary>
    /// Every named operation — "requeuedeadletters", "generatekey", "setroles", … — answers a caller
    /// with no token with 401 on whichever of its routes exists. A named handler is easy to add and
    /// easy to leave unguarded, and it is not reached by the resource-level calls above.
    /// </summary>
    [Theory]
    [MemberData(nameof(NamedHandlers))]
    public async Task Every_named_operation_refuses_a_caller_without_a_token(string resource, string name, string handler)
    {
        using var client = fixture.Client();
        var paths = new[] { $"/api/{resource}/{name}", $"/api/{resource}/1/{name}" };

        var responses = new List<(string Call, HttpStatusCode Status)>();
        foreach (var path in paths)
        {
            responses.Add(($"GET {path}", (await client.GetAsync(path)).StatusCode));
            responses.Add(($"POST {path}", (await client.PostAsync(path, Json())).StatusCode));
        }

        Assert.DoesNotContain(responses, r => (int)r.Status is >= 200 and < 300);
        Assert.True(responses.Any(r => r.Status == HttpStatusCode.Unauthorized),
            $"{handler}: no route answered 401 — {string.Join(", ", responses.Select(r => $"{r.Call} {(int)r.Status}"))}");
    }

    static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_token_from_sign_in_is_honoured_and_a_forged_one_is_not()
    {
        using var admin = await fixture.AdminAsync();
        var ok = await admin.GetAsync("/api/subscriptions");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var forged = fixture.Client();
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        var parts = token.Split('.');
        // Same header and claims, signature of another key.
        forged.DefaultRequestHeaders.Authorization = new("Bearer", $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_')}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/subscriptions")).StatusCode);
    }

    [Fact]
    public async Task The_public_config_answers_without_a_token()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/settings/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_rate_limited_per_address()
    {
        using var client = fixture.Client(forwardedFor: "203.0.113.77");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < HttpFixture.SignInPerMinute + 2; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/accounts/login",
                new { Username = "nobody@example.test", Password = "wrong" })).StatusCode);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(HttpFixture.SignInPerMinute));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses.Last());

        // Another address keeps its own budget.
        using var other = fixture.Client(forwardedFor: "203.0.113.78");
        Assert.NotEqual(HttpStatusCode.TooManyRequests,
            (await other.PostAsJsonAsync("/api/accounts/login", new { Username = "nobody@example.test", Password = "wrong" })).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/settings/config");

        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task Health_endpoints_answer_without_a_token()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.First()
        : response.Content.Headers.TryGetValues(name, out var content) ? content.First() : null!;
}
