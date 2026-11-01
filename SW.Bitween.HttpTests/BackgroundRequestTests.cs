using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The admin UI's long reads, carried on in the background when it sends <c>Prefer: respond-async</c>.
/// On a node whose database commands time out after two seconds, so that "longer than the usual
/// limit" takes seconds to show rather than half a minute.
/// </summary>
[Collection("Http")]
public class BackgroundRequestTests(HttpFixture fixture) : IAsyncLifetime
{
    WebApplicationFactory<Web.Program> node = null!;

    public Task InitializeAsync()
    {
        node = fixture.SecondNode(
            services => services.AddControllers().AddApplicationPart(typeof(SlowReadsController).Assembly),
            settings => settings["ConnectionStrings:BitweenDb"] += ";Command Timeout=2");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await node.DisposeAsync();

    [Fact]
    public async Task A_read_answered_within_the_wait_is_answered_as_usual()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var before = (await RowsAsync()).Length;

        var response = await admin.SendAsync(Read("/api/slowreads/0", wait: 5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (double)(await Api.Json(response))["seconds"]!);
        Assert.Equal(before, (await RowsAsync()).Length);
    }

    [Fact]
    public async Task A_read_that_does_not_ask_keeps_the_usual_limit()
    {
        using var admin = await HttpFixture.AdminOf(node);

        var response = await admin.SendAsync(Read("/api/slowreads/3"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task A_read_that_outlasts_the_wait_runs_past_the_usual_limit_and_its_answer_is_kept()
    {
        using var admin = await HttpFixture.AdminOf(node);

        var accepted = await admin.SendAsync(Read("/api/slowreads/3", wait: 0, label: "Exchanges: failed, last 7 days",
            page: "/exchanges?status=failed"));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var id = (string)(await Api.Json(accepted))["id"]!;
        Assert.Equal($"/api/background/{id}", accepted.Headers.Location?.ToString());

        var row = await FinishedAsync(admin, id);
        Assert.Equal("Done", (string)row["status"]!);
        Assert.Equal("Exchanges: failed, last 7 days", (string)row["label"]!);
        Assert.Equal("/exchanges?status=failed", (string)row["page"]!);
        Assert.Equal("/api/slowreads/3", (string)row["path"]!);

        var result = await admin.GetAsync($"/api/background/{id}/result");
        Assert.Equal(3, (double)(await Api.Json(result))["seconds"]!);
    }

    [Fact]
    public async Task An_api_read_in_the_background_answers_as_it_would_have_directly()
    {
        using var admin = await HttpFixture.AdminOf(node);

        // A list, and a refusal CqApi turns into a 404 of its own.
        foreach (var path in new[] { "/api/subscriptions", "/api/subscriptions/987654" })
        {
            var direct = await admin.GetAsync(path);

            // Made slow by holding its table: the read waits for the lock, past the caller's wait.
            var accepted = await fixture.InDbAsync(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE infolink.subscription IN ACCESS EXCLUSIVE MODE");
                var response = await admin.SendAsync(Read(path, wait: 1));
                await transaction.RollbackAsync();
                return response;
            });

            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var id = (string)(await Api.Json(accepted))["id"]!;
            Assert.Equal("Done", (string)(await FinishedAsync(admin, id))["status"]!);
            var kept = await admin.GetAsync($"/api/background/{id}/result");
            Assert.Equal(direct.StatusCode, kept.StatusCode);
            Assert.Equal(direct.Content.Headers.ContentType?.MediaType, kept.Content.Headers.ContentType?.MediaType);
            Assert.Equal(await direct.Content.ReadAsStringAsync(), await kept.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Cancelling_stops_the_query_the_database_is_running()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var id = (string)(await Api.Json(await admin.SendAsync(Read("/api/slowreads/60", wait: 0))))["id"]!;
        await SleepingAsync(1);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/background/{id}")).StatusCode);

        await SleepingAsync(0);
        Assert.Equal("Cancelled", (string)(await FinishedAsync(admin, id))["status"]!);
        Assert.Equal(HttpStatusCode.Gone, (await admin.GetAsync($"/api/background/{id}/result")).StatusCode);
    }

    [Fact]
    public async Task Three_of_a_persons_run_at_once_and_the_next_waits_its_turn()
    {
        using var admin = await HttpFixture.AdminOf(node);

        var started = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            await Api.Json(await admin.SendAsync(Read("/api/slowreads/2", wait: 0)))));

        Assert.Equal(["Running", "Running", "Running", "Waiting"], started.Select(s => (string)s["status"]!).Order());
        foreach (var request in started)
            Assert.Equal("Done", (string)(await FinishedAsync(admin, (string)request["id"]!))["status"]!);
    }

    [Fact]
    public async Task Its_owner_hears_it_finish_on_another_node()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var heard = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl("https://localhost/api/background/hub", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => fixture.App.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(admin.DefaultRequestHeaders.Authorization!.Parameter);
            })
            .Build();
        hub.On<JsonElement>("changed", change =>
        {
            if (change.GetProperty("status").GetString() == "Done") heard.TrySetResult(change.GetProperty("id").GetString()!);
        });
        await hub.StartAsync();

        var id = (string)(await Api.Json(await admin.SendAsync(Read("/api/slowreads/1", wait: 0))))["id"]!;

        Assert.Equal(id, await heard.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Nobody_else_sees_it_or_its_answer()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var id = (string)(await Api.Json(await admin.SendAsync(Read("/api/slowreads/1", wait: 0))))["id"]!;

        using var other = HttpFixture.ClientOf(node);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor("987654"));

        Assert.DoesNotContain((await Api.Json(await other.GetAsync("/api/background"))).AsArray(),
            r => (string)r!["id"]! == id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/background/{id}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/background/{id}")).StatusCode);
        Assert.Equal("Done", (string)(await FinishedAsync(admin, id))["status"]!);
    }

    [Fact]
    public async Task One_on_a_node_that_has_gone_shows_as_stopped()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var real = (string)(await Api.Json(await admin.SendAsync(Read("/api/slowreads/0", wait: 0))))["id"]!;
        await FinishedAsync(admin, real);
        var owner = (await RowsAsync()).Single(r => r.Id == real).Owner;

        var id = Guid.NewGuid().ToString("N");
        var startedOn = DateTime.UtcNow.AddMinutes(-5);
        await fixture.InDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO infolink.background_request (id, owner, path, node, status, started_on) VALUES ({id}, {owner}, {"/api/xchanges"}, {"gone-node:1"}, 1, {startedOn})"));

        var row = (await Api.Json(await admin.GetAsync("/api/background"))).AsArray().Single(r => (string)r!["id"]! == id)!;
        Assert.Equal("Stopped", (string)row["status"]!);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync($"/api/background/{id}/result")).StatusCode);
    }

    [Fact]
    public async Task Work_that_counts_its_steps_says_how_far_it_got_and_a_cancel_keeps_what_it_did()
    {
        using var admin = await HttpFixture.AdminOf(node);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/slowreads/steps") { Content = JsonContent.Create(new { count = 100 }) };
        request.Headers.TryAddWithoutValidation("Prefer", "respond-async, wait=0");
        var accepted = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var id = (string)(await Api.Json(accepted))["id"]!;

        // Said once a second at most, so seen within a few.
        JsonNode? row = null;
        for (var i = 0; i < 50 && (row == null || (int?)row["done"] is null or 0); i++)
        {
            await Task.Delay(100);
            row = (await Api.Json(await admin.GetAsync("/api/background"))).AsArray().Single(r => (string)r!["id"]! == id);
        }
        Assert.Equal(100, (int)row!["total"]!);
        Assert.True((int)row["done"]! > 0);
        Assert.Equal("POST /api/slowreads/steps", (string)row["path"]!);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/background/{id}")).StatusCode);

        var ended = await FinishedAsync(admin, id);
        Assert.Equal("Cancelled", (string)ended["status"]!);
        var result = await admin.GetAsync($"/api/background/{id}/result");
        var stoppedAt = (int)(await Api.Json(result))["done"]!;
        Assert.InRange(stoppedAt, 1, 99);
    }

    [Fact]
    public async Task An_export_in_the_background_is_kept_in_storage_and_handed_over_as_the_file()
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("export", key));
        foreach (var n in new[] { 1, 2 })
        {
            using var partner = fixture.Client();
            using var call = new HttpRequestMessage(HttpMethod.Post, $"/api/gateway/{urlName}/async")
            {
                Content = new StringContent($"{{\"order\":{n}}}", Encoding.UTF8, "application/json")
            };
            call.Headers.Add("partnerkey", key);
            Assert.Equal(HttpStatusCode.Accepted, (await partner.SendAsync(call)).StatusCode);
        }
        // An async call's exchange is made off the call, so it is waited for.
        var ids = new System.Collections.Generic.List<string>();
        for (var i = 0; i < 100 && ids.Count < 2; i++)
        {
            if (i > 0) await Task.Delay(100);
            ids = await fixture.InDbAsync(db => db.Set<Domain.Xchange>().Where(x => x.SubscriptionId == subscriptionId)
                .Select(x => x.Id).ToListAsync());
        }
        Assert.Equal(2, ids.Count);

        using var owner = await HttpFixture.AdminOf(node);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/xchanges/export") { Content = JsonContent.Create(new { ids }) };
        request.Headers.TryAddWithoutValidation("Prefer", "respond-async, wait=0");
        var accepted = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var id = (string)(await Api.Json(accepted))["id"]!;
        Assert.Equal("Done", (string)(await FinishedAsync(owner, id))["status"]!);

        var kept = await fixture.InDbAsync(db => db.Set<Domain.BackgroundRequest>().AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal($"background-requests/{id}", kept.ResultKey);
        Assert.Null(kept.Result);

        // Downloaded from a link the owner is given, by a browser that sends no sign-in.
        var link = (string)(await Api.Json(await owner.PostAsync($"/api/background/{id}/link", null)))["url"]!;
        using var browser = HttpFixture.ClientOf(node);
        var file = await browser.GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/zip", file.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("exchanges-", file.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var zip = new System.IO.Compression.ZipArchive(await file.Content.ReadAsStreamAsync());
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.Contains("/input")));

        // A link changed by a character, or to another request, opens nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(link[..^1] + (link[^1] == 'A' ? 'B' : 'A'))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(link.Replace(id, Guid.NewGuid().ToString("N")))).StatusCode);
        // And nobody else is handed one.
        using var other = HttpFixture.ClientOf(node);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor("987654"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/background/{id}/link", null)).StatusCode);
    }

    static HttpRequestMessage Read(string path, int? wait = null, string? label = null, string? page = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (wait != null) request.Headers.TryAddWithoutValidation("Prefer", $"respond-async, wait={wait}");
        if (label != null) request.Headers.Add("X-Bitween-Label", Uri.EscapeDataString(label));
        if (page != null) request.Headers.Add("X-Bitween-Page", Uri.EscapeDataString(page));
        return request;
    }

    /// <summary>The request, from the list, once it has stopped running.</summary>
    static async Task<JsonNode> FinishedAsync(HttpClient client, string id)
    {
        for (var i = 0; i < 300; i++)
        {
            var row = (await Api.Json(await client.GetAsync("/api/background"))).AsArray()
                .FirstOrDefault(r => (string)r!["id"]! == id);
            if (row != null && (string)row["status"]! is not ("Waiting" or "Running")) return row;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Background request {id} didn't finish.");
    }

    Task<Domain.BackgroundRequest[]> RowsAsync() =>
        fixture.InDbAsync(db => db.Set<Domain.BackgroundRequest>().AsNoTracking().ToArrayAsync());

    /// <summary>Waits until the database is running <paramref name="count"/> of the slow reads' sleeps.</summary>
    async Task SleepingAsync(int count)
    {
        for (var i = 0; i < 100 && await SleepsAsync() != count; i++) await Task.Delay(100);
        Assert.Equal(count, await SleepsAsync());
    }

    Task<int> SleepsAsync() => fixture.InDbAsync(db => db.Database
        .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE state = 'active' AND query LIKE 'SELECT pg_sleep(%'")
        .SingleAsync());

    /// <summary>A sign-in for an account that isn't the administrator, signed as this install signs them.</summary>
    string TokenFor(string accountId)
    {
        var configuration = node.Services.GetRequiredService<IConfiguration>();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, accountId)]),
            Issuer = configuration["Token:Issuer"],
            Audience = configuration["Token:Audience"],
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Token:Key"]!)), SecurityAlgorithms.HmacSha256),
        });
    }
}

/// <summary>A read as slow as asked: it sleeps in the database, as one on a big table waits on it.</summary>
[ApiController]
[Route("api/slowreads")]
public class SlowReadsController(BitweenDbContext db) : ControllerBase
{
    [HttpGet("{seconds:double}")]
    public async Task<IActionResult> Get(double seconds)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep({0})", seconds);
        return Ok(new { seconds });
    }

    /// <summary>
    /// Work that counts its steps, as a bulk retry does: a fifth of a second each, saying how far it got,
    /// and stopping between steps once its owner cancels it.
    /// </summary>
    [HttpPost("steps")]
    public async Task<IActionResult> Steps([FromBody] StepsRequest request)
    {
        var background = Services.Background.BackgroundRequestScope.Current;
        var done = 0;
        for (; done < request.Count; done++)
        {
            background?.Progress(done, request.Count, $"{done} of {request.Count}");
            if (background?.CancelRequested.IsCancellationRequested == true) break;
            await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.2)");
        }

        return Ok(new { done });
    }

    public class StepsRequest
    {
        public int Count { get; set; }
    }
}
