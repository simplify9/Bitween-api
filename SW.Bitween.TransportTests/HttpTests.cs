using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.HttpReceiver;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>What a partner's HTTP endpoint received.</summary>
public record Received(string Method, string Path, string Body, string? ContentType, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// A partner's HTTP endpoints, served by a real Kestrel on a loopback port: it records each request
/// and answers as the path asks — success, a bad request, too many requests, a failure, slowness,
/// and the token endpoints the adapters sign in with.
/// </summary>
public sealed class PartnerServer : IAsyncLifetime
{
    WebApplication app = null!;
    public string Url { get; private set; } = null!;
    public ConcurrentQueue<Received> Requests { get; } = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        app = builder.Build();

        app.Use(async (context, next) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            Requests.Enqueue(new Received(context.Request.Method, context.Request.Path + context.Request.QueryString, body,
                context.Request.ContentType,
                context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
            context.Items["body"] = body;
            await next();
        });

        app.MapPost("/orders/{id}", (string id) => Results.Json(new { accepted = id }, statusCode: 201));
        app.MapPost("/bad", () => Results.Json(new { error = "order has no lines" }, statusCode: 400));
        app.MapPost("/busy", (HttpContext c) =>
        {
            c.Response.Headers.RetryAfter = "30";
            return Results.StatusCode(429);
        });
        app.MapPost("/broken", () => Results.Text("upstream exploded", statusCode: 502));
        app.MapPost("/slow", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return Results.Ok();
        });
        app.MapPost("/login", () => Results.Json(new { jwt = "login-token" }));
        app.MapPost("/oauth/token", (HttpContext c) =>
            ((string)c.Items["body"]!).Contains("grant_type=client_credentials") &&
            ((string)c.Items["body"]!).Contains("client_id=bitween")
                ? Results.Json(new { access_token = "oauth-token", expires_in = 3600 })
                : Results.StatusCode(401));
        app.MapPost("/secure", (HttpContext c) =>
            c.Request.Headers.Authorization.ToString() is "Bearer login-token" or "Bearer oauth-token"
                ? Results.Json(new { ok = true })
                : Results.StatusCode(401));
        app.MapGet("/shipments", (HttpContext c) =>
            c.Request.Headers["ApiKey"] == "partner-key"
                ? Results.Json(new { data = new { items = new object[] { new { id = 1, carrier = "DHL" }, new { id = 2, carrier = "UPS" } } } })
                : Results.StatusCode(401));
        app.MapGet("/single", () => Results.Json(new { id = 7 }));
        app.MapGet("/down", () => Results.StatusCode(503));

        await app.StartAsync();
        Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public Received Last(string pathStart) => Requests.Last(r => r.Path.StartsWith(pathStart));

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>The HTTP handler and receiver against a real HTTP server.</summary>
public class HttpTests(PartnerServer partner) : IClassFixture<PartnerServer>
{
    Dictionary<string, string> Settings(string path, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string> { ["Url"] = partner.Url + path, ["TimeoutSeconds"] = "5" };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    [Fact]
    public async Task A_delivery_posts_the_message_to_the_url_its_values_fill_in_with_its_credentials_and_headers()
    {
        var handler = Adapters.Create<NativeHttpHandler>(Settings("/orders/{{orderId}}",
            ("AuthType", "Basic"), ("LoginUsername", "acme"), ("LoginPassword", "s3cret"),
            ("ContentType", "application/json"), ("Headers", "X-Tenant:acme,X-Trace:abc-123")));

        var response = await handler.Handle(new XchangeFile("{\"orderId\":\"SO-42\",\"lines\":3}"));

        Assert.False(response.BadData);
        Assert.Equal("{\"accepted\":\"SO-42\"}", response.Data);
        var received = partner.Last("/orders/SO-42");
        Assert.Equal("POST", received.Method);
        Assert.Equal("{\"orderId\":\"SO-42\",\"lines\":3}", received.Body);
        Assert.StartsWith("application/json", received.ContentType);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("acme:s3cret")), received.Headers["Authorization"]);
        Assert.Equal("acme", received.Headers["X-Tenant"]);
        Assert.Equal("abc-123", received.Headers["X-Trace"]);
    }

    [Theory]
    [InlineData("X-Tenant: acme, X-Trace: abc-123")]
    [InlineData("X-Tenant:acme,\nX-Trace:abc-123,")]
    [InlineData("X-Tenant=acme\nX-Trace=abc-123")]
    public async Task Headers_are_sent_however_the_list_is_spaced_or_separated(string headers)
    {
        var path = $"/orders/h{Guid.NewGuid():N}";
        await Adapters.Create<NativeHttpHandler>(Settings(path, ("Headers", headers)))
            .Handle(new XchangeFile("{}"));

        var received = partner.Last(path);
        Assert.Equal("acme", received.Headers["X-Tenant"]);
        Assert.Equal("abc-123", received.Headers["X-Trace"]);
    }

    [Fact]
    public async Task A_header_without_a_value_is_named_in_the_failure()
    {
        var failure = await Assert.ThrowsAsync<SWException>(() =>
            Adapters.Create<NativeHttpHandler>(Settings("/orders/x", ("Headers", "X-Tenant"))).Handle(new XchangeFile("{}")));
        Assert.Contains("'X-Tenant'", failure.Message);
    }

    [Fact]
    public async Task A_rejection_is_returned_as_a_bad_response_not_a_failure()
    {
        var response = await Adapters.Create<NativeHttpHandler>(Settings("/bad")).Handle(new XchangeFile("{}"));

        Assert.True(response.BadData);
        Assert.Contains("order has no lines", response.Data);
    }

    [Theory]
    [InlineData("/busy", "Retry-After: 30s")]
    [InlineData("/broken", "upstream exploded")]
    public async Task Not_now_and_a_server_failure_fail_the_delivery_so_it_can_be_retried(string path, string expected)
    {
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            Adapters.Create<NativeHttpHandler>(Settings(path)).Handle(new XchangeFile("{}")));
        Assert.Contains(expected, failure.Message);
    }

    [Fact]
    public async Task An_endpoint_that_does_not_answer_in_time_fails_the_delivery()
    {
        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            Adapters.Create<NativeHttpHandler>(Settings("/slow", ("TimeoutSeconds", "1"))).Handle(new XchangeFile("{}")));
        Assert.Contains("did not answer within 1 seconds", failure.Message);
    }

    [Theory]
    [InlineData("Login", "/login")]
    [InlineData("OAuth2", "/oauth/token")]
    public async Task A_token_is_fetched_from_the_sign_in_endpoint_and_sent_as_the_bearer(string authType, string loginPath)
    {
        var handler = Adapters.Create<NativeHttpHandler>(Settings("/secure",
            ("AuthType", authType), ("LoginUrl", partner.Url + loginPath),
            ("LoginUsername", "acme"), ("LoginPassword", "s3cret"),
            ("ClientId", "bitween"), ("ClientSecret", "oauth-secret")));

        var response = await handler.Handle(new XchangeFile("{}"));

        Assert.False(response.BadData);
        Assert.Equal("{\"ok\":true}", response.Data);
    }

    [Fact]
    public async Task The_receiver_pulls_the_list_with_its_key_and_hands_over_each_element()
    {
        var receiver = Adapters.Create<NativeHttpReceiver>(Settings("/shipments",
            ("AuthType", "ApiKey"), ("ApiKey", "partner-key"), ("ArrayPath", "$.data.items")));
        await receiver.Initialize();

        var ids = (await receiver.ListFiles()).ToList();

        Assert.Equal(2, ids.Count);
        var first = await receiver.GetFile(ids[0]);
        Assert.Contains("\"carrier\": \"DHL\"", first.Data);
        Assert.Contains("\"carrier\": \"UPS\"", (await receiver.GetFile(ids[1])).Data);
        await receiver.DeleteFile(ids[0]);
        Assert.Equal("GET", partner.Last("/shipments").Method);
    }

    [Fact]
    public async Task A_single_object_is_one_file_and_an_unavailable_source_fails_the_run()
    {
        var receiver = Adapters.Create<NativeHttpReceiver>(Settings("/single"));
        Assert.Single(await receiver.ListFiles());

        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.Create<NativeHttpReceiver>(Settings("/down")).ListFiles());
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Adapters.Create<NativeHttpReceiver>(Settings("/shipments", ("AuthType", "ApiKey"), ("ApiKey", "wrong"))).ListFiles());
    }
}
