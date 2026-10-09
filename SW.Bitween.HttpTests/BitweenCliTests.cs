using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
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
        Console.SetOut(output);
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

    public void Dispose()
    {
        try { Directory.Delete(work, true); } catch { }
    }
}
