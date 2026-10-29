using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// bitween mapping test against a Bitween: a subscription's saved mapping maps each case's input,
/// and the output is compared with the expected file, as JSON.
/// </summary>
[Collection("Http")]
public class MappingTestTests(HttpFixture fixture) : IDisposable
{
    static readonly SemaphoreSlim OneAtATime = new(1, 1);
    readonly string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bitween-mapping-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(work, true); } catch { }
    }

    sealed class FromAddress(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        readonly string address = $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Forwarded-For", address);
            return base.SendAsync(request, cancellationToken);
        }
    }

    async Task<Profiles> SignedInAsync()
    {
        var profiles = new Profiles(Path.Combine(work, "cli.json"));
        var (exit, output) = await Bitween(profiles, HttpFixture.AdminPassword + "\n",
            "login", "https://localhost", "--email", HttpFixture.AdminEmail, "--password-stdin");
        Assert.True(exit == 0, output);
        return profiles;
    }

    async Task<(int Exit, string Output)> Bitween(Profiles profiles, string stdin, params string[] args)
    {
        await OneAtATime.WaitAsync();
        var original = Console.Out;
        using var output = new StringWriter();
        var synchronized = TextWriter.Synchronized(output);
        Console.SetOut(synchronized);
        BitweenApi.HandlerOverride = () => new FromAddress(fixture.App.Server.CreateHandler());
        try
        {
            var exit = await Program.RunAsync(args, profiles, new StringReader(stdin ?? ""), () => "");
            string text;
            lock (synchronized) text = output.ToString();
            return (exit, string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("{\"@t\""))));
        }
        finally
        {
            BitweenApi.HandlerOverride = null;
            Console.SetOut(original);
            OneAtATime.Release();
        }
    }

    const string Rules = """
        { "version": 1, "sourceFormat": "json", "targetFormat": "json",
          "fields": [ { "target": ["reference"], "from": { "kind": "path", "path": "order.id" } },
                      { "target": ["customer"], "from": { "kind": "path", "path": "order.customer" }, "transform": { "fn": "upper" } } ] }
        """;

    async Task<string> SubscriptionWithMapping()
    {
        var admin = await fixture.AdminAsync();
        var name = Api.Unique("Mapping test");
        var documentId = await Api.CreateAsync(admin, "/api/documents", new { name = Api.Unique("Order"), documentFormat = "Json" });
        var id = await Api.CreateAsync(admin, "/api/subscriptions", new { name, documentId, type = "GatewayApiCall" });
        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{id}/savemapper", new
        {
            mapperId = "NativeMapper",
            mapperProperties = new[] { new { key = "MappingRules", value = Rules } },
        }));
        return name;
    }

    string Cases(string subscription, string expected)
    {
        File.WriteAllText(Path.Combine(work, "order.json"), """{ "order": { "id": "SO-1", "customer": "acme" } }""");
        File.WriteAllText(Path.Combine(work, "order.expected.json"), expected);
        var file = Path.Combine(work, "cases.yaml");
        File.WriteAllText(file, $"""
            cases:
              - name: An order maps to the warehouse's shape
                subscription: {subscription}
                input: order.json
                expected: order.expected.json
            """);
        return file;
    }

    [Fact]
    public async Task A_mapping_that_gives_what_is_expected_passes()
    {
        var profiles = await SignedInAsync();
        var file = Cases(await SubscriptionWithMapping(), """{ "customer": "ACME", "reference": "SO-1" }""");

        var (exit, output) = await Bitween(profiles, null, "mapping", "test", file);

        Assert.True(exit == 0, output);
        Assert.Contains("PASS  An order maps to the warehouse's shape", output);
    }

    [Fact]
    public async Task A_difference_fails_and_says_where()
    {
        var profiles = await SignedInAsync();
        var file = Cases(await SubscriptionWithMapping(), """{ "customer": "acme", "reference": "SO-1" }""");

        var (exit, output) = await Bitween(profiles, null, "mapping", "test", file);

        Assert.Equal(1, exit);
        Assert.Contains("FAIL", output);
        Assert.Contains("$.customer is \"ACME\"", output);
    }

    [Fact]
    public async Task Update_writes_the_expected_file()
    {
        var profiles = await SignedInAsync();
        var file = Cases(await SubscriptionWithMapping(), "{}");

        var (exit, output) = await Bitween(profiles, null, "mapping", "test", file, "--update");

        Assert.True(exit == 0, output);
        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(work, "order.expected.json")))!;
        Assert.Equal("ACME", (string)written["customer"]!);
    }
}
