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
using SW.Bitween.Services.Marketplace;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The registry check: a newer version found with its release notes, an installed version the signed
/// revocation list names flagged, and a list that doesn't verify changing nothing.
/// </summary>
[Collection("Http")]
public class RegistryUpdateTests(HttpFixture fixture) : IDisposable
{
    readonly Ed25519PrivateKeyParameters registryKey = new(new SecureRandom());
    readonly Ed25519PrivateKeyParameters publisherKey = new(new SecureRandom());
    readonly string id = $"s9.lab.updates{Guid.NewGuid().ToString("N")[..8]}";

    // What the fake registry says, changed as the test goes.
    string[] published = ["1.0.0"];
    object[] revoked = [];
    bool badRevocationSignature;

    public void Dispose()
    {
        RegistryClient.HandlerOverride = null;
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

    AdapterManifest Manifest(string version) => new()
    {
        Id = id, Version = version, Kinds = ["handler"], Runtime = AdapterManifest.PythonRuntime, Entry = "main.py",
        DisplayName = "Updates test", Summary = "For tests.", ReleaseNotes = $"What changed in {version}.",
        Compatibility = new AdapterCompatibility { Applications = new() { ["bitween"] = "1.0.0" } },
    };

    byte[] Package(string version)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry(AdapterManifest.FileName).Open())) w.Write(Manifest(version).ToJson());
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

    void Serve()
    {
        var packages = new[] { "1.0.0", "1.1.0", "1.2.0" }.ToDictionary(v => v, Package);
        string Sha(string v) => Convert.ToHexStringLower(SHA256.HashData(packages[v]));
        var publisherPublic = publisherKey.GeneratePublicKey();
        var publisherKeyId = RegistrySignatures.KeyIdOf(publisherPublic);
        var signedOn = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

        RegistryClient.HandlerOverride = () => new FakeRegistry(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/revocations")
            {
                var payload = JsonSerializer.Serialize(new { generatedOn = "2026-10-13T10:00:00Z", revoked });
                var key = badRevocationSignature ? new Ed25519PrivateKeyParameters(new SecureRandom()) : registryKey;
                return Ok(new { payload, keyId = "test", signature = Sign(key, RegistrySignatures.RevocationsMessage(payload)) });
            }
            if (path == $"/v1/packages/{id}")
                return Ok(new
                {
                    card = new { id, displayName = "Updates test", latest = published.Last(), publisher = "s9", tier = "Official", pricing = "Free" },
                    versions = published.Reverse().Select(v => new { version = v, status = "Published", sha256 = Sha(v) }),
                });
            foreach (var v in published)
            {
                if (path == $"/v1/packages/{id}/versions/{v}/download")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packages[v]) };
                if (path == $"/v1/packages/{id}/versions/{v}")
                    return Ok(new
                    {
                        packageId = id, version = v, status = "Published", sha256 = Sha(v), size = packages[v].Length,
                        manifest = JsonDocument.Parse(Manifest(v).ToJson()).RootElement,
                        publisher = "s9", tier = "Official", pricing = "Free", publisherKeyId,
                        publisherPublicKey = PublicText(publisherPublic),
                        publisherSignature = Sign(publisherKey, RegistrySignatures.PublishMessage(id, v, Sha(v))),
                        registryKeyId = "test",
                        registrySignature = Sign(registryKey, RegistrySignatures.CountersignMessage(id, v, Sha(v), "s9", publisherKeyId, "Official", signedOn)),
                        signedOn,
                    });
            }
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

    JsonObject Mine(JsonObject summary) =>
        summary["adapters"]!.AsArray().Select(a => a!.AsObject()).Single(a => (string)a["adapterId"]! == id);

    [Fact]
    public async Task A_newer_version_and_a_revoked_one_are_found_and_a_bad_list_changes_nothing()
    {
        Serve();
        using var admin = await fixture.AdminAsync();
        await Json(await admin.PostAsJsonAsync("/api/marketplace/install", new { id, version = "1.0.0", current = true }));

        // Nothing newer yet.
        var first = await Json(await admin.PostAsync("/api/marketplace/check", JsonContent.Create(new { })));
        Assert.Null((string)first["error"]);
        Assert.False((bool)Mine(first)["updateAvailable"]!);
        Assert.Empty(Mine(first)["revoked"]!.AsArray());

        // Two newer versions, newest first with their notes; 1.0.0 revoked.
        published = ["1.0.0", "1.1.0", "1.2.0"];
        revoked = [new { packageId = id, version = "1.0.0", sha256 = "", revokedOn = "2026-10-13T11:00:00Z", reason = "Sends the key in the clear." }];
        var second = Mine(await Json(await admin.PostAsync("/api/marketplace/check", JsonContent.Create(new { }))));
        Assert.True((bool)second["updateAvailable"]!);
        Assert.Equal("1.2.0", (string)second["latest"]);
        Assert.Equal(["1.2.0", "1.1.0"], second["newer"]!.AsArray().Select(n => (string)n!["version"]!).ToArray());
        Assert.Equal("What changed in 1.2.0.", (string)second["newer"]![0]!["releaseNotes"]);
        var flag = second["revoked"]!.AsArray().Single()!;
        Assert.Equal("1.0.0", (string)flag["version"]);
        Assert.Equal("Sends the key in the clear.", (string)flag["reason"]);

        // A list that doesn't verify: the check says so, and the flags from the last good one stay.
        badRevocationSignature = true;
        revoked = [];
        var third = await Json(await admin.PostAsync("/api/marketplace/check", JsonContent.Create(new { })));
        Assert.Contains("isn't signed by a trusted key", (string)third["error"]);
        Assert.Single(Mine(third)["revoked"]!.AsArray());

        // Read back as stored, as the Adapters page reads it.
        var stored = await Json(await admin.GetAsync("/api/marketplace/updates"));
        Assert.True((bool)stored["configured"]!);
        Assert.NotNull((string)stored["checkedOn"]);
        Assert.Single(Mine(stored)["revoked"]!.AsArray());
        Assert.Equal("1.0.0", (string)Mine(stored)["current"]);
    }

    [Fact]
    public async Task Checking_needs_operate_and_reading_needs_view()
    {
        using var admin = await fixture.AdminAsync();
        var email = $"updates-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Updates-Member-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Updates member", email, password, roleIds = new[] { 2 } });
        using var member = fixture.Client();
        member.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            await HttpFixture.SignInAsync(member, email, password));

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/marketplace/updates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/marketplace/check", JsonContent.Create(new { }))).StatusCode);
    }
}
