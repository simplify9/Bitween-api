extern alias BouncyCastle2;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using BouncyCastle2::Org.BouncyCastle.Crypto.Signers;
using BouncyCastle2::Org.BouncyCastle.Security;
using BouncyCastle2::Org.BouncyCastle.X509;
using SW.Bitween.Cli;
using SW.Bitween.Services.Marketplace;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// A kit installed from the Marketplace tab, against a registry played here: its package checked as
/// an adapter's is, then planned and applied as the caller through Bitween's own API.
/// </summary>
[Collection("Http")]
public class KitMarketplaceTests(HttpFixture fixture) : IDisposable
{
    readonly Ed25519PrivateKeyParameters registryKey = new(new SecureRandom());
    readonly Ed25519PrivateKeyParameters publisherKey = new(new SecureRandom());
    readonly string s = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        RegistryClient.HandlerOverride = null;
        BitweenApi.HandlerOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        foreach (var key in new[] { "Bitween.RegistryTrustedKeys", "Bitween.RegistryUrl", "Bitween.RegistryEnabled" })
            admin.DeleteAsync($"/api/settings/{key}").GetAwaiter().GetResult();
    }

    static string Sign(Ed25519PrivateKeyParameters key, string message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        var bytes = Encoding.UTF8.GetBytes(message);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    static string PublicText(Ed25519PublicKeyParameters key) =>
        Convert.ToBase64String(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key).GetDerEncoded());

    string Id => $"s9.lab.kit{s}";

    /// <summary>A kit as bitween kit build writes it: itself under source/, a manifest of kind kit with its parameters.</summary>
    byte[] KitPackage()
    {
        var kitYaml = $"apiVersion: bitween/v1\nkind: Kit\nmetadata: {{ id: {Id}, version: 1.0.0, displayName: Lab kit, summary: For tests. }}\n" +
                      "parameters:\n  - { name: Label, description: Names }\n  - { name: Url, description: Where to send, default: https://crm.example.test }\n";
        var bundle = "apiVersion: bitween/v1\nkind: Configuration\ninformationTypes:\n  - name: ${param:Label} contact\n    format: Json\n" +
                     "subscriptions:\n  - name: ${param:Label} out\n    type: GatewayApiCall\n    informationType: ${param:Label} contact\n" +
                     "    handler:\n      adapter: NativeHttpHandler\n      properties:\n        Url: ${param:Url}\n";
        string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var manifest = new JsonObject
        {
            ["manifestVersion"] = 1, ["id"] = Id, ["version"] = "1.0.0", ["displayName"] = "Lab kit", ["summary"] = "For tests.",
            ["kinds"] = new JsonArray("kit"),
            ["compatibility"] = new JsonObject { ["applications"] = new JsonObject { ["bitween"] = "1.0.0" } },
            ["source"] = new JsonObject { ["path"] = "source", ["files"] = new JsonObject { ["kit.yaml"] = Sha(kitYaml), ["bundle/main.yaml"] = Sha(bundle) } },
            ["kit"] = new JsonObject
            {
                ["connectors"] = new JsonArray(),
                ["parameters"] = new JsonArray(
                    new JsonObject { ["name"] = "Label", ["description"] = "Names", ["required"] = true, ["secret"] = false },
                    new JsonObject { ["name"] = "Url", ["description"] = "Where to send", ["required"] = true, ["secret"] = false, ["default"] = "https://crm.example.test" }),
            },
        };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string path, string text) { using var w = new StreamWriter(zip.CreateEntry(path).Open()); w.Write(text); }
            Write("adapter.json", manifest.ToJsonString());
            Write("source/kit.yaml", kitYaml);
            Write("source/bundle/main.yaml", bundle);
        }
        return buffer.ToArray();
    }

    sealed class FakeRegistry(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    void Serve(byte[] package)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(package));
        var publisherPublic = publisherKey.GeneratePublicKey();
        var publisherKeyId = RegistrySignatures.KeyIdOf(publisherPublic);
        var signedOn = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
        var revocations = """{"generatedOn":"2026-10-13T10:00:00Z","revoked":[]}""";
        var manifest = JsonDocument.Parse(new StreamReader(new ZipArchive(new MemoryStream(package)).GetEntry("adapter.json")!.Open()).ReadToEnd()).RootElement;

        RegistryClient.HandlerOverride = () => new FakeRegistry(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/download")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
            object body = path switch
            {
                "/v1/revocations" => new { payload = revocations, keyId = "test", signature = Sign(registryKey, RegistrySignatures.RevocationsMessage(revocations)) },
                _ when path == $"/v1/packages/{Id}/versions/1.0.0" => new
                {
                    packageId = Id, version = "1.0.0", status = "Published", sha256 = sha, size = package.Length, manifest,
                    publisher = "s9", tier = "Official", pricing = "Free", publisherKeyId, publisherPublicKey = PublicText(publisherPublic),
                    publisherSignature = Sign(publisherKey, RegistrySignatures.PublishMessage(Id, "1.0.0", sha)),
                    registryKeyId = "test",
                    registrySignature = Sign(registryKey, RegistrySignatures.CountersignMessage(Id, "1.0.0", sha, "s9", publisherKeyId, "Official", signedOn)),
                    signedOn,
                },
                _ => null,
            };
            return body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        });

        // Saved as an administrator saves them, so a settings reload mid-test keeps them.
        using (var admin = fixture.AdminAsync().GetAwaiter().GetResult())
        {
            void Save(string key, string value) =>
                admin.PostAsJsonAsync($"/api/settings/{key}", new { value }).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            Save("Bitween.RegistryEnabled", "true");
            Save("Bitween.RegistryUrl", "https://registry.test");
            Save("Bitween.RegistryTrustedKeys", PublicText(registryKey.GeneratePublicKey()));
        }
        // Bitween reaches its own API in process here, as it would over its listening address.
        BitweenApi.HandlerOverride = () => fixture.App.Server.CreateHandler();
    }

    static async Task<JsonObject> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    [Fact]
    public async Task A_kit_is_described_planned_and_installed_from_the_marketplace_as_its_caller()
    {
        Serve(KitPackage());
        using var admin = await fixture.AdminAsync();
        var label = "Lab" + s;

        var info = await Json(await admin.GetAsync($"/api/marketplace/kit?id={Id}&version=1.0.0"));
        Assert.Equal(["Label", "Url"], info["parameters"]!.AsArray().Select(p => (string)p!["name"]!).ToArray());
        Assert.Equal("https://crm.example.test", (string)info["parameters"]![1]!["default"]!);

        var request = new { id = Id, version = "1.0.0", parameters = new { Label = label } };
        var plan = await Json(await admin.PostAsJsonAsync("/api/marketplace/kitplan", request));
        Assert.True((bool)plan["ready"]!, plan.ToJsonString());
        Assert.False((bool)plan["applied"]!);
        Assert.Contains(plan["lines"]!.AsArray(), l => ((string)l!).Contains($"+ information type \"{label} contact\""));
        Assert.DoesNotContain((await Json(await admin.GetAsync("/api/documents?page=0&size=1000")))["result"]!.AsArray(),
            d => (string)d!["name"]! == $"{label} contact");

        var installed = await Json(await admin.PostAsJsonAsync("/api/marketplace/kitinstall", request));
        Assert.True((bool)installed["applied"]!, installed.ToJsonString());
        Assert.Equal(Id, (string)installed["name"]!);
        Assert.Contains((await Json(await admin.GetAsync("/api/documents?page=0&size=1000")))["result"]!.AsArray(),
            d => (string)d!["name"]! == $"{label} contact");

        // Installed again as it is: nothing to change.
        var again = await Json(await admin.PostAsJsonAsync("/api/marketplace/kitinstall", request));
        Assert.Contains(again["lines"]!.AsArray(), l => ((string)l!).Contains("No changes"));
    }

    [Fact]
    public async Task A_parameter_left_out_installs_nothing_and_a_kit_is_never_installed_as_an_adapter()
    {
        Serve(KitPackage());
        using var admin = await fixture.AdminAsync();

        var run = await Json(await admin.PostAsJsonAsync("/api/marketplace/kitinstall", new { id = Id, version = "1.0.0" }));
        Assert.False((bool)run["ready"]!);
        Assert.False((bool)run["applied"]!);
        Assert.Contains("Label", run["problems"]!.ToJsonString());

        var asAdapter = await admin.PostAsJsonAsync("/api/marketplace/install", new { id = Id, version = "1.0.0" });
        Assert.Equal(HttpStatusCode.BadRequest, asAdapter.StatusCode);
        Assert.Contains("REGISTRY_KIT", await asAdapter.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_kit_whose_download_isnt_what_was_signed_installs_nothing()
    {
        var package = KitPackage();
        Serve(package);
        var signedSha = Convert.ToHexStringLower(SHA256.HashData(package));
        // Same listing, other bytes.
        var tampered = package.Concat(new byte[] { 0 }).ToArray();
        var original = RegistryClient.HandlerOverride!;
        RegistryClient.HandlerOverride = () => new FakeRegistry(r => r.RequestUri!.AbsolutePath.EndsWith("/download")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tampered) }
            : new HttpMessageInvoker(original()).SendAsync(r, CancellationToken.None).Result);
        Assert.NotEqual(signedSha, Convert.ToHexStringLower(SHA256.HashData(tampered)));

        using var admin = await fixture.AdminAsync();
        var refused = await admin.PostAsJsonAsync("/api/marketplace/kitinstall", new { id = Id, version = "1.0.0", parameters = new { Label = "X" + s } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("REGISTRY_SIGNATURE", await refused.Content.ReadAsStringAsync());
    }
}
