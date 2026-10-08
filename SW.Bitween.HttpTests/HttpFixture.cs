using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SW.Bus;
using SW.Bitween.Services;
using SW.EfCoreExtensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The whole application, hosted in-process and called over HTTP: routing, model binding, JSON,
/// authentication, the rate limiter and the response headers all run as they do in production —
/// everything the integration suite skips by calling handlers directly. Started exactly as
/// Program.Main starts it: migrations, then the seeded-administrator, system-key, key-hashing and
/// stored-settings steps.
/// </summary>
public sealed class HttpFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@Bitween.systems";
    public const string AdminPassword = "Http-Tests-Admin-2026!";
    public const int SignInPerMinute = 30;
    public const int RetryCount = 2;

    readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
    // With the management plugin, as production runs it: Ops pages and queue clean-up go through it.
    readonly RabbitMqContainer rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management")
        .WithPortBinding(15672, true)
        .Build();
    readonly string bucket = $"bitween-http-tests-{Guid.NewGuid():N}";

    public WebApplicationFactory<Web.Program> App { get; private set; } = null!;
    Dictionary<string, string?> settings = null!;

    /// <summary>
    /// Another instance of the app on the same database and broker, as a second node in a cluster
    /// is. The caller disposes it.
    /// </summary>
    public WebApplicationFactory<Web.Program> SecondNode()
    {
        var node = new BitweenApp(settings);
        using var _ = node.CreateClient();
        return node;
    }

    /// <summary>A client of <paramref name="node"/> with no credentials.</summary>
    public static HttpClient ClientOf(WebApplicationFactory<Web.Program> node)
    {
        var client = node.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    /// <summary>The broker the app consumes from, for a test to look at or publish to directly.</summary>
    public string RabbitConnectionString => rabbit.GetConnectionString();

    /// <summary>The app's own database context, to check what an API call left behind.</summary>
    public async Task<T> InDbAsync<T>(Func<BitweenDbContext, Task<T>> query)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
    }

    /// <summary>The broker's queues, by name.</summary>
    public async Task<string[]> QueuesAsync() =>
        (await RabbitCtlAsync("list_queues", "name")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Runs rabbitmqctl in the broker's container.</summary>
    public async Task<string> RabbitCtlAsync(params string[] args) =>
        (await rabbit.ExecAsync(["rabbitmqctl", "-q", .. args])).Stdout;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());

        settings = new Dictionary<string, string?>
        {
            ["Bitween:DatabaseType"] = "PgSql",
            ["ConnectionStrings:BitweenDb"] = postgres.GetConnectionString(),
            ["ConnectionStrings:RabbitMQ"] = rabbit.GetConnectionString(),
            ["Bitween:RabbitMqManagementUrl"] = $"http://{rabbit.Hostname}:{rabbit.GetMappedPublicPort(15672)}",
            ["Bitween:RabbitMqManagementUsername"] = RabbitMqBuilder.DefaultUsername,
            ["Bitween:RabbitMqManagementPassword"] = RabbitMqBuilder.DefaultPassword,
            ["Bitween:StorageProvider"] = "Local",
            ["CloudFiles:BucketName"] = bucket,
            ["Token:Key"] = "http-tests-signing-key-0123456789abcdefghijklmnop",
            ["Token:Issuer"] = "http-tests",
            ["Token:Audience"] = "http-tests",
            ["Bitween:InitialAdminPassword"] = AdminPassword,
            ["Bitween:RateLimits:SignInPerMinute"] = SignInPerMinute.ToString(),
            ["Bitween:RateLimits:RequestsPerMinute"] = "100000",
        };

        App = new BitweenApp(settings);
        // Creating a client starts the host.
        using var _ = App.CreateClient();

        // The seeded administrator must change its password at first sign-in; doing it once here
        // gives every test a session that can do something.
        using var client = Client();
        var token = await SignInAsync(client, AdminEmail, AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var changed = await client.PostAsJsonAsync("/api/accounts/changePassword",
            new { OldPassword = AdminPassword, NewPassword = AdminPassword });
        changed.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (App != null) await App.DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), rabbit.DisposeAsync().AsTask());
        try { Directory.Delete(Path.Combine(Path.GetTempPath(), "SW.CloudFiles.LocalTests", bucket), true); } catch { }
    }

    /// <summary>A client with no credentials, from its own address so rate limits don't leak between tests.</summary>
    public HttpClient Client(string forwardedFor = null!)
    {
        var client = App.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor ?? $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    public static async Task<string> SignInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/accounts/login", new { Username = email, Password = password });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("jwt").GetString()!;
    }

    /// <summary>A client signed in as the administrator.</summary>
    public async Task<HttpClient> AdminAsync()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client, AdminEmail, AdminPassword));
        return client;
    }

    sealed class BitweenApp(IDictionary<string, string?> settings) : WebApplicationFactory<Web.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = builder.Build();
            // As Program.Main does before Run.
            host.MigrateDatabase<BitweenDbContext>();
            host.SecureSeededAdministrator().SecureSystemPartnerKey().HashStoredPartnerKeys().ApplyStoredSettings();
            // Production retries a failed message five times a minute apart. Twice, a second apart, is
            // the same path through the retry queue to the bad one, in a time a test can wait for.
            var bus = host.Services.GetRequiredService<BusOptions>();
            bus.DefaultRetryCount = RetryCount;
            bus.DefaultRetryAfter = 1;
            host.Start();
            return host;
        }
    }
}

[CollectionDefinition("Http")]
public class HttpCollection : ICollectionFixture<HttpFixture>;
