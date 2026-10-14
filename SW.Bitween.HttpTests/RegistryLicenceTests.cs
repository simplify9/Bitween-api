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
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Services.Marketplace;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Paid connectors: a Bitween registers with a code and from then on sends its token; a paid
/// connector installs only on a licence the registry signed, and stops running once the licence
/// is past its grace; a licence that doesn't verify is dropped; the usage report says only counts.
/// </summary>
[Collection("Http")]
public class RegistryLicenceTests(HttpFixture fixture) : IDisposable
{
    readonly Ed25519PrivateKeyParameters registryKey = new(new SecureRandom());
    readonly Ed25519PrivateKeyParameters publisherKey = new(new SecureRandom());
    readonly string id = $"s9.lab.paid{Guid.NewGuid().ToString("N")[..8]}";
    const string Code = "bwreg_test-code";
    const string Token = "bwi_test-token-0123456789";

    // What the fake registry serves, changed as the test goes.
    object[] licences = [];
    string lastAuthorization;
    JsonElement? usageSent;

    public void Dispose()
    {
        RegistryClient.HandlerOverride = null;
        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        foreach (var key in new[] { "Bitween.RegistryTrustedKeys", "Bitween.RegistryUrl", "Bitween.RegistryEnabled",
                     "Bitween.RegistryInstanceId", "Bitween.RegistryInstanceToken", "Bitween.RegistryUsageReports" })
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

    /// <summary>A licence as the registry issues it: the document, signed over the licence message.</summary>
    object Licence(DateTime starts, DateTime ends, int graceDays, string[] packages = null, string[] instances = null, bool badSignature = false)
    {
        var payload = JsonSerializer.Serialize(new
        {
            version = 1, licenceId = 7, customer = new { id = 1, name = "Acme" },
            instances = instances ?? ["*"], packages = packages ?? [id],
            starts = starts.ToString("yyyy-MM-ddTHH:mm:ssZ"), ends = ends.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            graceDays, issuedOn = "2026-10-14T10:00:00Z",
        });
        var key = badSignature ? new Ed25519PrivateKeyParameters(new SecureRandom()) : registryKey;
        return new { payload, keyId = "test", signature = Sign(key, RegistrySignatures.LicenceMessage(payload)) };
    }

    AdapterManifest Manifest => new()
    {
        Id = id, Version = "1.0.0", Kinds = ["handler"], Runtime = AdapterManifest.PythonRuntime, Entry = "main.py",
        DisplayName = "Paid test", Summary = "For tests.",
        Compatibility = new AdapterCompatibility { Applications = new() { ["bitween"] = "1.0.0" } },
    };

    byte[] Package()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry(AdapterManifest.FileName).Open())) w.Write(Manifest.ToJson());
            using (var w = new StreamWriter(archive.CreateEntry("main.py").Open())) w.Write("print('hello')\n");
        }
        return zip.ToArray();
    }

    sealed class FakeRegistry(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    static HttpResponseMessage Refused(HttpStatusCode status, string code, string message) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(new { code, message }), Encoding.UTF8, "application/json") };

    void Serve()
    {
        var package = Package();
        var sha = Convert.ToHexStringLower(SHA256.HashData(package));
        var publisherPublic = publisherKey.GeneratePublicKey();
        var publisherKeyId = RegistrySignatures.KeyIdOf(publisherPublic);
        var signedOn = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

        RegistryClient.HandlerOverride = () => new FakeRegistry(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            lastAuthorization = request.Headers.Authorization?.ToString();
            var bearer = lastAuthorization == $"Bearer {Token}";

            if (path == "/v1/instances/register" && request.Method == HttpMethod.Post)
            {
                var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
                if (body.GetProperty("code").GetString() != Code) return Refused(HttpStatusCode.BadRequest, "CODE_INVALID", "The registration code is unknown, used or expired. Ask for a new one.");
                return Ok(new { instanceId = "bwi-test", token = Token, displayName = body.GetProperty("displayName").GetString(), customerId = 1, customerName = "Acme" });
            }
            if (path == "/v1/instances/me/entitlements")
                return bearer ? Ok(licences) : Refused(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Sign in.");
            if (path == "/v1/instances/me/usage" && request.Method == HttpMethod.Post)
            {
                if (!bearer) return Refused(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Sign in.");
                usageSent = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/v1/revocations")
            {
                var payload = JsonSerializer.Serialize(new { generatedOn = "2026-10-13T10:00:00Z", revoked = Array.Empty<object>() });
                return Ok(new { payload, keyId = "test", signature = Sign(registryKey, RegistrySignatures.RevocationsMessage(payload)) });
            }
            if (path == $"/v1/packages/{id}")
                return Ok(new
                {
                    card = new { id, displayName = "Paid test", latest = "1.0.0", publisher = "s9", tier = "Official", pricing = "Paid" },
                    versions = new[] { new { version = "1.0.0", status = "Published", sha256 = sha } },
                });
            if (path == $"/v1/packages/{id}/versions/1.0.0/download")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
            if (path == $"/v1/packages/{id}/versions/1.0.0")
                return Ok(new
                {
                    packageId = id, version = "1.0.0", status = "Published", sha256 = sha, size = package.Length,
                    manifest = JsonDocument.Parse(Manifest.ToJson()).RootElement,
                    publisher = "s9", tier = "Official", pricing = "Paid", publisherKeyId,
                    publisherPublicKey = PublicText(publisherPublic),
                    publisherSignature = Sign(publisherKey, RegistrySignatures.PublishMessage(id, "1.0.0", sha)),
                    registryKeyId = "test",
                    registrySignature = Sign(registryKey, RegistrySignatures.CountersignMessage(id, "1.0.0", sha, "s9", publisherKeyId, "Official", signedOn)),
                    signedOn,
                });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var admin = fixture.AdminAsync().GetAwaiter().GetResult();
        void Save(string key, string value) =>
            admin.PostAsJsonAsync($"/api/settings/{key}", new { value }).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        Save("Bitween.RegistryEnabled", "true");
        Save("Bitween.RegistryUrl", "https://registry.test");
        Save("Bitween.RegistryTrustedKeys", PublicText(registryKey.GeneratePublicKey()));
    }

    static async Task<JsonObject> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    static async Task<string> Refusal(HttpResponseMessage response, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.False(response.IsSuccessStatusCode, text);
        Assert.Contains(code, text);
        return text;
    }

    [Fact]
    public async Task A_paid_connector_installs_on_a_licence_and_stops_once_it_is_past_grace()
    {
        Serve();
        using var admin = await fixture.AdminAsync();

        // Unregistered: a paid connector is refused, and nothing was sent as a token.
        await Refusal(await admin.PostAsJsonAsync("/api/marketplace/install", new { id, version = "1.0.0", current = true }), "REGISTRY_LICENCE");

        // A wrong code is the registry's refusal, said as it said it.
        var wrong = await Refusal(await admin.PostAsJsonAsync("/api/marketplace/register", new { code = "bwreg_wrong" }), "REGISTRY_REFUSED");
        Assert.Contains("unknown, used or expired", wrong);

        // Registered: the id and the token are settings now, the token masked, and the licences were fetched.
        var now = DateTime.UtcNow;
        licences = [Licence(now.AddDays(-1), now.AddDays(30), 14)];
        var registered = await Json(await admin.PostAsJsonAsync("/api/marketplace/register", new { code = Code, displayName = "Lab" }));
        Assert.Equal("bwi-test", (string)registered["instanceId"]);
        Assert.Equal("Acme", (string)registered["customerName"]);
        Assert.Equal($"Bearer {Token}", lastAuthorization);
        var held = registered["licences"]!.AsObject();
        Assert.True((bool)held["registered"]!);
        Assert.Equal("Live", (string)held["licences"]![0]!["state"]);
        var settings = await (await admin.GetAsync("/api/settings")).Content.ReadAsStringAsync();
        Assert.Contains("Bitween.RegistryInstanceToken", settings);
        Assert.DoesNotContain(Token, settings);

        // Licensed: it installs, and the catalog remembers it is paid.
        var installed = await Json(await admin.PostAsJsonAsync("/api/marketplace/install", new { id, version = "1.0.0", current = true }));
        Assert.Equal("1.0.0", (string)installed["version"]);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<Services.Adapters.AdapterCatalog>();
            var origin = (await catalog.GetAsync(id))!.Extensions![MarketplaceInstaller.OriginExtension];
            Assert.Equal("paid", origin.GetProperty("pricing").GetString());
            await scope.ServiceProvider.GetRequiredService<RegistryLicences>().EnsureLicensedAsync(id);
        }

        // A licence that doesn't verify is dropped and said so; the one held before stays.
        licences = [Licence(now.AddDays(-1), now.AddDays(30), 14, badSignature: true)];
        var refreshed = await Json(await admin.PostAsync("/api/marketplace/refreshlicences", JsonContent.Create(new { })));
        Assert.Contains("didn't verify", (string)refreshed["error"]);
        Assert.Empty(refreshed["licences"]!.AsArray());

        // Past its grace: the connector may not run, with a reason a retry can wait on.
        licences = [Licence(now.AddDays(-60), now.AddDays(-20), 14)];
        var expired = await Json(await admin.PostAsync("/api/marketplace/refreshlicences", JsonContent.Create(new { })));
        Assert.Equal("Expired", (string)expired["licences"]![0]!["state"]);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var check = scope.ServiceProvider.GetRequiredService<RegistryLicences>();
            var ex = await Assert.ThrowsAsync<SWValidationException>(() => check.EnsureLicensedAsync(id));
            Assert.Contains("ADAPTER_LICENCE_EXPIRED", ex.ToString());
            Assert.Contains("grace is over", ex.Message);
        }
        // Installing it again now is refused for the licence, before anything else is looked at.
        await Refusal(await admin.PostAsJsonAsync("/api/marketplace/install", new { id, version = "1.0.0", current = false }), "REGISTRY_LICENCE");

        // In grace: it runs again, and the licence list says Grace.
        licences = [Licence(now.AddDays(-60), now.AddDays(-5), 14)];
        var grace = await Json(await admin.PostAsync("/api/marketplace/refreshlicences", JsonContent.Create(new { })));
        Assert.Equal("Grace", (string)grace["licences"]![0]!["state"]);
        using (var scope = fixture.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RegistryLicences>().EnsureLicensedAsync(id);

        // The usage report: a preview says what would go, and sending needs the setting on.
        var preview = await Json(await admin.PostAsJsonAsync("/api/marketplace/usage", new { send = false }));
        Assert.NotNull(preview["day"]);
        Assert.NotNull(preview["lines"]);
        await Refusal(await admin.PostAsJsonAsync("/api/marketplace/usage", new { send = true }), "USAGE_OFF");
        (await admin.PostAsJsonAsync("/api/settings/Bitween.RegistryUsageReports", new { value = "true" })).EnsureSuccessStatusCode();
        await Json(await admin.PostAsJsonAsync("/api/marketplace/usage", new { send = true }));
        if (usageSent is { } sent)
        {
            // Only counts went: ids, versions, subscriptions, calls, failures.
            foreach (var line in sent.GetProperty("lines").EnumerateArray())
                Assert.Equal(["packageId", "version", "subscriptions", "calls", "failures"], line.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    /// <summary>A bundle as the registry writes it, for this test's package, with the bytes given as the package.</summary>
    byte[] Bundle(byte[] package, object[] bundledLicences, bool tamper = false)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(package));
        var publisherPublic = publisherKey.GeneratePublicKey();
        var publisherKeyId = RegistrySignatures.KeyIdOf(publisherPublic);
        var signedOn = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
        var revocations = JsonSerializer.Serialize(new { generatedOn = "2026-10-13T10:00:00Z", revoked = Array.Empty<object>() });

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string name, byte[] bytes) { using var s = zip.CreateEntry(name).Open(); s.Write(bytes); }
            void WriteText(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));
            Write($"packages/{id}-1.0.0.zip", tamper ? [.. package, 0] : package);
            WriteText("bundle.json", JsonSerializer.Serialize(new
            {
                version = 1, registry = "https://registry.test", madeOn = "2026-10-14T12:00:00Z",
                packages = new[]
                {
                    new
                    {
                        id, version = "1.0.0", file = $"packages/{id}-1.0.0.zip", sha256 = sha, size = package.Length,
                        publisher = "s9", tier = "Official", pricing = "Paid", publisherKeyId,
                        publisherPublicKey = PublicText(publisherPublic),
                        publisherSignature = Sign(publisherKey, RegistrySignatures.PublishMessage(id, "1.0.0", sha)),
                        registryKeyId = "test",
                        registrySignature = Sign(registryKey, RegistrySignatures.CountersignMessage(id, "1.0.0", sha, "s9", publisherKeyId, "Official", signedOn)),
                        signedOn = "2026-10-13T10:00:00Z",
                        manifest = JsonDocument.Parse(Manifest.ToJson()).RootElement,
                    },
                },
            }));
            WriteText("keys.json", "{}");
            WriteText("revocations.json", JsonSerializer.Serialize(new { payload = revocations, keyId = "test", signature = Sign(registryKey, RegistrySignatures.RevocationsMessage(revocations)) }));
            WriteText("licences.json", JsonSerializer.Serialize(bundledLicences));
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_bundle_installs_offline_with_its_licences_and_a_tampered_package_is_refused()
    {
        Serve();
        using var admin = await fixture.AdminAsync();
        // The marketplace is off, as an offline site has it; only the keys in Settings matter.
        (await admin.PostAsJsonAsync("/api/settings/Bitween.RegistryEnabled", new { value = "false" })).EnsureSuccessStatusCode();
        var now = DateTime.UtcNow;
        var package = Package();

        async Task<JsonObject> Install(byte[] bundle)
        {
            using var content = new ByteArrayContent(bundle);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            return await Json(await admin.PostAsync("/api/marketplace/bundle?current=true", content));
        }

        // Paid, and the bundle carries no licence: refused, and the other checks aren't what refused it.
        var unlicensed = await Install(Bundle(package, []));
        var item = unlicensed["items"]!.AsArray().Single()!;
        Assert.Equal("Refused", (string)item["outcome"]);
        Assert.Contains("no licence", (string)item["message"]);

        // Tampered bytes: refused for the signature, whatever the licence says.
        var tampered = await Install(Bundle(package, [Licence(now.AddDays(-1), now.AddDays(30), 14)], tamper: true));
        Assert.Equal("Refused", (string)tampered["items"]![0]!["outcome"]);
        Assert.Contains("doesn't match what the registry signed", (string)tampered["items"]![0]!["message"]);

        // With its licence: installed, the licence now held, the origin the bundle's registry.
        var installed = await Install(Bundle(package, [Licence(now.AddDays(-1), now.AddDays(30), 14)]));
        Assert.Equal("https://registry.test", (string)installed["registry"]);
        Assert.Equal("Installed", (string)installed["items"]![0]!["outcome"]);
        var held = await Json(await admin.GetAsync("/api/marketplace/licences"));
        Assert.Equal("Live", (string)held["licences"]![0]!["state"]);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<Services.Adapters.AdapterCatalog>();
            var origin = (await catalog.GetAsync(id))!.Extensions![MarketplaceInstaller.OriginExtension];
            Assert.Equal("https://registry.test", origin.GetProperty("url").GetString());
            Assert.Equal("paid", origin.GetProperty("pricing").GetString());
        }

        // Again: skipped, not an error.
        var again = await Install(Bundle(package, []));
        Assert.Equal("Skipped", (string)again["items"]![0]!["outcome"]);

        // Not a bundle at all.
        var notZip = await admin.PostAsync("/api/marketplace/bundle?current=true", new ByteArrayContent(Encoding.UTF8.GetBytes("hello")));
        await Refusal(notZip, "BUNDLE_INVALID");
    }

    [Fact]
    public async Task Registering_needs_settings_edit_and_licences_need_view()
    {
        Serve();
        using var admin = await fixture.AdminAsync();
        var email = $"licences-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Licences-Member-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Licences member", email, password, roleIds = new[] { 2 } });
        using var viewer = fixture.Client();
        viewer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            await HttpFixture.SignInAsync(viewer, email, password));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/marketplace/register", new { code = Code })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/marketplace/refreshlicences", JsonContent.Create(new { }))).StatusCode);
        var read = await Json(await viewer.GetAsync("/api/marketplace/licences"));
        Assert.False((bool)read["registered"]!);
    }
}
