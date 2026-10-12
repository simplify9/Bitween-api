using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>
/// A partner that has stopped answering properly, on a loopback port of its own so its circuit is
/// nobody else's: 503 or no answer at all until told to recover. It counts what reaches it.
/// </summary>
public sealed class AilingPartner : IAsyncLifetime
{
    WebApplication app = null!;
    public string Url { get; private set; } = null!;
    public int Calls;
    public volatile bool Healthy;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        app = builder.Build();

        app.Use(async (context, next) =>
        {
            Interlocked.Increment(ref Calls);
            if (Healthy) context.Response.StatusCode = 200;
            else await next();
        });
        app.MapPost("/down", () => Results.Text("maintenance", statusCode: 503));
        app.MapPost("/hang", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return Results.Ok();
        });

        await app.StartAsync();
        Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>The HTTP handler's circuit breaker against a real HTTP server that keeps failing.</summary>
public class CircuitBreakerTests(AilingPartner partner) : IClassFixture<AilingPartner>
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    readonly Clock clock = new();

    /// <summary>The adapters as the app registers them, with a breaker of three failures and a clock the test turns.</summary>
    Func<string, int, NativeHttpHandler> Handlers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<TimeProvider>(clock);
        services.AddNativeAdapters(circuitBreaker: _ => new CircuitBreakerSettings(Failures: 3, WindowSeconds: 60, OpenSeconds: 60));
        var provider = services.BuildServiceProvider();

        return (path, timeout) =>
        {
            // A scope per delivery, as the app has: the breaker still has to be the same one.
            var handler = provider.CreateScope().ServiceProvider.GetServices<INativeAdapter>().OfType<NativeHttpHandler>().First();
            handler.InitializeStartupValues(new Dictionary<string, string> { ["Url"] = partner.Url + path, ["TimeoutSeconds"] = timeout.ToString() });
            return handler;
        };
    }

    [Fact]
    public async Task After_three_503s_the_handler_fails_at_once_without_calling_and_recovers_after_the_cool_down()
    {
        partner.Healthy = false;
        var handler = Handlers();

        for (var i = 0; i < 3; i++)
        {
            var failure = await Assert.ThrowsAsync<Exception>(() => handler("/down", 5).Handle(new XchangeFile("{}")));
            Assert.StartsWith("503: ServiceUnavailable: maintenance", failure.Message);
        }
        var before = partner.Calls;

        var started = DateTime.UtcNow;
        var open = await Assert.ThrowsAsync<CircuitOpenException>(() => handler("/down", 5).Handle(new XchangeFile("{}")));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        Assert.StartsWith($"503: ServiceUnavailable: circuit open for {partner.Url} (3 failures in a row; trying again after ", open.Message);
        Assert.Equal(before, partner.Calls);

        partner.Healthy = true;
        await Assert.ThrowsAsync<CircuitOpenException>(() => handler("/down", 5).Handle(new XchangeFile("{}")));
        Assert.Equal(before, partner.Calls);

        clock.Now = clock.Now.AddSeconds(60);
        await handler("/down", 5).Handle(new XchangeFile("{}"));
        await handler("/down", 5).Handle(new XchangeFile("{}"));
        Assert.Equal(before + 2, partner.Calls);
    }

    [Fact]
    public async Task After_three_timeouts_the_handler_stops_waiting_on_a_partner_that_never_answers()
    {
        partner.Healthy = false;
        var handler = Handlers();

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<TimeoutException>(() => handler("/hang", 1).Handle(new XchangeFile("{}")));
        var before = partner.Calls;

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<CircuitOpenException>(() => handler("/hang", 1).Handle(new XchangeFile("{}")));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500), "failed without waiting for the timeout");
        Assert.Equal(before, partner.Calls);

        // The trial fails too, and the circuit opens again for another cool-down.
        clock.Now = clock.Now.AddSeconds(60);
        await Assert.ThrowsAsync<TimeoutException>(() => handler("/hang", 1).Handle(new XchangeFile("{}")));
        var reopened = await Assert.ThrowsAsync<CircuitOpenException>(() => handler("/hang", 1).Handle(new XchangeFile("{}")));
        Assert.Contains("the trial call failed", reopened.Message);
        Assert.Equal(before + 1, partner.Calls);
    }
}
