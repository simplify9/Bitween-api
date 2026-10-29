using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// POST /api/kits/plan: a kit sent as its zip, planned and nothing changed, as the registry checks each kit
/// published against a reference Bitween. Its examples stand in for the parameters it isn't given.
/// </summary>
[Collection("Http")]
public class KitPlanTests(HttpFixture fixture) : IDisposable
{
    readonly string s = Guid.NewGuid().ToString("N")[..8];

    public void Dispose() => BitweenApi.HandlerOverride = null;

    string KitYaml => $"apiVersion: bitween/v1\nkind: Kit\nmetadata: {{ id: s9.lab.plan{s}, version: 1.0.0, displayName: Plan kit, summary: For tests. }}\n" +
                      "parameters:\n  - { name: Label, description: Names, default: Plan }\n" +
                      "  - { name: Url, description: Where to send, example: https://crm.example.test }\n";

    string Bundle(string url = "${param:Url}", string informationType = "${param:Label} contact") =>
        "apiVersion: bitween/v1\nkind: Configuration\ninformationTypes:\n  - name: ${param:Label} contact\n    format: Json\n" +
        $"subscriptions:\n  - name: ${{param:Label}} out\n    type: GatewayApiCall\n    informationType: {informationType}\n" +
        $"    handler:\n      adapter: NativeHttpHandler\n      properties:\n        Url: \"{url}\"\n";

    static byte[] Zip(string kitYaml, string bundle)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string path, string text) { using var w = new StreamWriter(zip.CreateEntry(path).Open()); w.Write(text); }
            Write("source/kit.yaml", kitYaml);
            Write("source/bundle/main.yaml", bundle);
        }
        return buffer.ToArray();
    }

    async Task<JsonObject> Plan(HttpClient client, byte[] zip, string query)
    {
        BitweenApi.HandlerOverride = () => fixture.App.Server.CreateHandler();
        var response = await client.PostAsync("/api/kits/plan" + query, new ByteArrayContent(zip));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    static string[] Strings(JsonNode list) => list!.AsArray().Select(l => (string)l!).ToArray();

    [Fact]
    public async Task A_kit_plans_with_its_examples_and_nothing_is_made()
    {
        using var admin = await fixture.AdminAsync();
        var label = "Plan" + s;

        var plan = await Plan(admin, Zip(KitYaml, Bundle()), $"?examples=true&set=Label={label}");
        Assert.True((bool)plan["ready"]!, plan.ToJsonString());
        Assert.False((bool)plan["applied"]!);
        Assert.Contains(Strings(plan["lines"]), l => l.Contains($"+ information type \"{label} contact\""));
        Assert.Contains(Strings(plan["lines"]), l => l.Contains($"+ subscription \"{label} out\""));

        var documents = JsonNode.Parse(await admin.GetStringAsync("/api/documents?page=0&size=1000"))!["result"]!.AsArray();
        Assert.DoesNotContain(documents, d => (string)d!["name"]! == $"{label} contact");
    }

    [Fact]
    public async Task Without_examples_a_parameter_left_out_is_named()
    {
        using var admin = await fixture.AdminAsync();
        var plan = await Plan(admin, Zip(KitYaml, Bundle()), "");
        Assert.False((bool)plan["ready"]!);
        Assert.Contains(Strings(plan["problems"]), p => p.Contains("Url"));
    }

    [Fact]
    public async Task What_doesnt_resolve_and_what_a_kit_may_not_read_are_problems()
    {
        using var admin = await fixture.AdminAsync();

        var unresolved = await Plan(admin, Zip(KitYaml, Bundle(informationType: "Nowhere" + s)), "?examples=true");
        Assert.False((bool)unresolved["ready"]!);
        Assert.Contains(Strings(unresolved["problems"]), p => p.Contains("Nowhere" + s));

        var reads = await Plan(admin, Zip(KitYaml, Bundle(url: "https://x.test/?k=${env:Token__Key}")), "?examples=true");
        Assert.False((bool)reads["ready"]!);
        Assert.Contains(Strings(reads["problems"]), p => p.Contains("reads values only from its parameters"));
    }

    [Fact]
    public async Task Not_a_zip_is_said_and_a_member_is_refused()
    {
        using var admin = await fixture.AdminAsync();
        var notZip = await Plan(admin, "not a zip"u8.ToArray(), "?examples=true");
        Assert.Contains(Strings(notZip["problems"]), p => p.Contains("isn't a zip"));

        var email = $"kitplan-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "KitPlan-Member-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Kit plan member", email, password, roleIds = new[] { 2 } });
        using var member = fixture.Client();
        member.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            await HttpFixture.SignInAsync(member, email, password));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/kits/plan", new ByteArrayContent(Zip(KitYaml, Bundle())))).StatusCode);
    }
}
