using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Cli;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The bitween CLI against a Bitween, through its real entry point: sign in, write a Python adapter,
/// check it against the Bitween contract, publish it through the API, list, promote and withdraw its
/// versions — and a Member who signs in refused the publishing.
/// </summary>
[Collection("Http")]
public class BitweenCliTests(HttpFixture fixture) : IDisposable
{
    static readonly SemaphoreSlim OneAtATime = new(1, 1);
    readonly string work = Path.Combine(Path.GetTempPath(), "bitween-cli-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The test server's own handler, from an address of its own so sign-in limits don't leak between tests.</summary>
    sealed class FromAddress(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        readonly string address = $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Forwarded-For", address);
            return base.SendAsync(request, cancellationToken);
        }
    }

    async Task<(int Exit, string Output)> Bitween(Profiles profiles, string stdin, params string[] args)
    {
        await OneAtATime.WaitAsync();
        var original = Console.Out;
        using var output = new StringWriter();
        // The in-process server logs to the console from its own threads while the CLI writes.
        Console.SetOut(TextWriter.Synchronized(output));
        BitweenApi.HandlerOverride = () => new FromAddress(fixture.App.Server.CreateHandler());
        try
        {
            var exit = await Program.RunAsync(args, profiles, new StringReader(stdin ?? ""), () => "");
            // The in-process server logs to the same console as JSON lines; what the CLI said is the rest.
            var said = string.Join("\n", output.ToString().Split('\n').Where(l => !l.TrimStart().StartsWith("{\"@t\"")));
            return (exit, said);
        }
        finally
        {
            BitweenApi.HandlerOverride = null;
            Console.SetOut(original);
            OneAtATime.Release();
        }
    }

    [Fact]
    public async Task An_adapter_goes_from_init_to_published_and_current_through_the_api()
    {
        var profiles = new Profiles(Path.Combine(work, "cli.json"));
        var login = await Bitween(profiles, HttpFixture.AdminPassword + "\n",
            "login", "https://localhost", "--email", HttpFixture.AdminEmail, "--password-stdin");
        Assert.True(login.Exit == 0, login.Output);
        Assert.Contains("Signed in to https://localhost", login.Output);

        var name = "CliPy" + Guid.NewGuid().ToString("N")[..8];
        var id = SW.Serverless.Tooling.Scaffolding.Scaffolder.IdFrom(name);
        Assert.Equal(0, (await Bitween(profiles, null, "adapter", "init", name, "--kind", "handler", "--lang", "python", "--dir", work)).Exit);
        var project = Path.Combine(work, name);
        File.WriteAllText(Path.Combine(work, "settings.json"), """{ "ApiKey": "k" }""");

        var test = await Bitween(profiles, null, "adapter", "test", project, "--settings", Path.Combine(work, "settings.json"));
        Assert.True(test.Exit == 0, test.Output);
        Assert.Contains("PASS bitween handler: Handle answers example 1", test.Output);

        var build = await Bitween(profiles, null, "adapter", "build", project);
        Assert.True(build.Exit == 0, build.Output);
        var zip = Path.Combine(project, "bin", "serverless", $"{id}-0.1.0.zip");

        var published = await Bitween(profiles, null, "adapter", "publish", zip, "--notes", "From the CLI");
        Assert.True(published.Exit == 0, published.Output);
        Assert.Contains($"Published {id} 0.1.0", published.Output);
        Assert.Contains("It isn't current", published.Output);

        var current = await Bitween(profiles, null, "adapter", "publish", zip, "-v", "minor", "--current");
        Assert.Contains($"Published {id} 0.2.0", current.Output);

        var versions = await Bitween(profiles, null, "adapter", "versions", id);
        Assert.Contains("* 0.2.0", versions.Output);
        Assert.Contains("  0.1.0", versions.Output);

        Assert.Equal(0, (await Bitween(profiles, null, "adapter", "promote", id, "0.1.0")).Exit);
        Assert.Equal(0, (await Bitween(profiles, null, "adapter", "withdraw", id, "0.2.0")).Exit);
        versions = await Bitween(profiles, null, "adapter", "versions", id);
        Assert.Contains("* 0.1.0", versions.Output);
        Assert.Contains("0.2.0", versions.Output.Split('\n').Single(l => l.Contains("0.2.0")));
        Assert.Contains("withdrawn", versions.Output.Split('\n').Single(l => l.Contains("0.2.0")));

        // In Bitween, as any published adapter: listed, current at 0.1.0, with its source.
        using var admin = await fixture.AdminAsync();
        var catalog = await Api.Json(await admin.GetAsync("/api/adapters/Catalog?prefix=handlers"));
        var row = catalog.AsArray().Single(a => (string)a!["key"]! == id)!;
        Assert.Equal("0.1.0", (string)row["currentVersion"]!);
        Assert.Equal("From the CLI", (string)row["versionHistory"]!.AsArray().Single(v => (string)v!["version"]! == "0.1.0")!["releaseNotes"]!);
        Assert.True((bool)row["versionHistory"]![0]!["hasSource"]!);

        var trail = await Api.Json(await admin.GetAsync("/api/audit?entityName=AdapterRelease&limit=200"));
        var actions = trail["result"]!.AsArray().Where(r => (string?)r!["changes"]?["AdapterId"]?["new"] == id)
            .Select(r => (string)r!["changes"]!["Action"]!["new"]! + " " + (string)r["changes"]!["Version"]!["new"]!).OrderBy(a => a).ToArray();
        Assert.Equal(["promoted 0.1.0", "promoted 0.2.0", "published 0.1.0", "published 0.2.0", "withdrawn 0.2.0"], actions);

        // And it runs, on a node with data sources off — the default: Python adapters run on the
        // resident host, which every node has.
        using (var scope = fixture.App.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<SW.PrimitiveTypes.IServerlessService>();
            await service.StartAsync(id, "cli-test", new System.Collections.Generic.Dictionary<string, string> { ["ApiKey"] = "k" });
            try
            {
                var answer = await service.InvokeAsync<Newtonsoft.Json.Linq.JObject>("Handle", new { Data = "ping", Filename = "p.txt" });
                Assert.Equal("ping", (string)answer["Data"]);
            }
            finally
            {
                ((IDisposable)service).Dispose();
            }
        }

        Assert.Equal(0, (await Bitween(profiles, null, "logout")).Exit);
        Assert.NotEqual(0, (await Bitween(profiles, null, "adapter", "versions", id)).Exit);
    }

    [Fact]
    public async Task A_member_can_sign_in_and_build_but_not_publish()
    {
        using var admin = await fixture.AdminAsync();
        var email = $"cli-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Cli-Member-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Cli member", email, password, roleIds = new[] { 2 } });

        var profiles = new Profiles(Path.Combine(work, "member.json"));
        Assert.Equal(0, (await Bitween(profiles, password + "\n", "login", "https://localhost", "--email", email, "--password-stdin")).Exit);
        var whoami = await Bitween(profiles, null, "whoami");
        Assert.Contains("No adapter permissions", whoami.Output);

        var name = "CliRefused" + Guid.NewGuid().ToString("N")[..6];
        await Bitween(profiles, null, "adapter", "init", name, "--lang", "python", "--dir", work);
        var build = await Bitween(profiles, null, "adapter", "build", Path.Combine(work, name));
        Assert.True(build.Exit == 0, build.Output);
        var zip = Directory.GetFiles(Path.Combine(work, name, "bin", "serverless"), "*.zip").Single();

        var refused = await Bitween(profiles, null, "adapter", "publish", zip);
        Assert.Equal(1, refused.Exit);
        Assert.Contains("doesn't have the permission", refused.Output);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_nothing_is_kept()
    {
        var profiles = new Profiles(Path.Combine(work, "wrong.json"));
        var login = await Bitween(profiles, "not-the-password\n", "login", "https://localhost", "--email", HttpFixture.AdminEmail, "--password-stdin");
        Assert.Equal(1, login.Exit);
        Assert.Contains("Invalid username or password", login.Output);
        Assert.False(File.Exists(profiles.Path));
    }

    /// <summary>
    /// Plays the browser: signs in to the admin UI as the administrator, confirms the CLI's request
    /// on the cli-login page (the grant), and follows the redirect back to the CLI's loopback.
    /// </summary>
    bool ConfirmInBrowser(string address)
    {
        _ = Task.Run(async () =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(address).Query);
            using var admin = await fixture.AdminAsync();
            var grant = await admin.PostAsJsonAsync("/api/accounts/cligrant", new { codeChallenge = query["challenge"] });
            var code = (await grant.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["code"]!.ToString();
            using var loopback = new HttpClient();
            await loopback.GetAsync($"http://127.0.0.1:{query["port"]}/callback?code={Uri.EscapeDataString(code)}&state={query["state"]}");
        });
        return true;
    }

    [Fact]
    public async Task Signing_in_through_the_browser_works_and_logout_ends_the_session_on_the_server()
    {
        var profiles = new Profiles(Path.Combine(work, "browser.json"));
        BrowserSignIn.OpenOverride = ConfirmInBrowser;
        try
        {
            var login = await Bitween(profiles, null, "login", "https://localhost");
            Assert.True(login.Exit == 0, login.Output);
            Assert.Contains($"as {HttpFixture.AdminEmail}", login.Output);
        }
        finally
        {
            BrowserSignIn.OpenOverride = null;
        }

        var whoami = await Bitween(profiles, null, "whoami");
        Assert.True(whoami.Exit == 0, whoami.Output);
        Assert.Contains(HttpFixture.AdminEmail, whoami.Output);

        var refreshToken = profiles.Find().Profile.RefreshToken;
        Assert.False(string.IsNullOrEmpty(refreshToken));

        var logout = await Bitween(profiles, null, "logout");
        Assert.True(logout.Exit == 0, logout.Output);
        Assert.Contains("Signed out of", logout.Output);

        // The token the CLI held no longer renews a session anywhere.
        var renew = await fixture.Client().PostAsJsonAsync("/api/accounts/login", new { refreshToken });
        Assert.False(renew.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_sign_in_code_is_refused_without_its_verifier_or_when_altered()
    {
        var verifier = new string('v', 43);
        var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var admin = await fixture.AdminAsync();
        var grant = await admin.PostAsJsonAsync("/api/accounts/cligrant", new { codeChallenge = challenge });
        Assert.True(grant.IsSuccessStatusCode);
        var code = (await grant.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["code"]!.ToString();

        // Not a bearer token: the code can't be used to call the API.
        using var withCode = fixture.Client();
        withCode.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", code);
        Assert.Equal(401, (int)(await withCode.GetAsync("/api/accounts/profile")).StatusCode);

        using var client = fixture.Client();
        Assert.False((await client.PostAsJsonAsync("/api/accounts/clitoken", new { code, codeVerifier = new string('w', 43) })).IsSuccessStatusCode);
        var altered = code[..^2] + (code[^2] == 'A' ? "B" : "A") + code[^1];
        Assert.False((await client.PostAsJsonAsync("/api/accounts/clitoken", new { code = altered, codeVerifier = verifier })).IsSuccessStatusCode);

        var redeemed = await client.PostAsJsonAsync("/api/accounts/clitoken", new { code, codeVerifier = verifier });
        Assert.True(redeemed.IsSuccessStatusCode);
        var session = (await redeemed.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!;
        Assert.Equal(HttpFixture.AdminEmail, session["email"]!.ToString());
        Assert.False(string.IsNullOrEmpty(session["refreshToken"]?.ToString()));

        // Signed out, a grant needs a session.
        Assert.Equal(401, (int)(await fixture.Client().PostAsJsonAsync("/api/accounts/cligrant", new { codeChallenge = challenge })).StatusCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(work, true); } catch { }
    }
}
