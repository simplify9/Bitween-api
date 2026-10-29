extern alias BouncyCastle2;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using BouncyCastle2::Org.BouncyCastle.Pkcs;
using BouncyCastle2::Org.BouncyCastle.Security;
using SW.Bitween.Cli;
using SW.Bitween.Services.Marketplace;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// bitween registry against a registry played here: login keeps the token and the key's path, publish
/// signs what Bitween's own check verifies, and the registry's answers (in review, a failed check,
/// already there, a bad token) come out as exit codes CI can act on.
/// </summary>
[Collection("Http")]
public class RegistryCliTests : IDisposable
{
    const string Token = "bwr_test-token";
    readonly string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bitween-registry-cli", Guid.NewGuid().ToString("N"))).FullName;
    readonly Ed25519PrivateKeyParameters key = new(new SecureRandom());
    readonly List<(string Id, string Version, string Sha256, string KeyId, string Signature)> received = [];
    readonly Profiles profiles;

    public RegistryCliTests()
    {
        profiles = new Profiles(Path.Combine(work, "cli.json"));
        RegistryApi.HandlerOverride = () => new FakeRegistry(Answer);
    }

    public void Dispose()
    {
        RegistryApi.HandlerOverride = null;
        try { Directory.Delete(work, true); } catch { /* temporary */ }
    }

    sealed class FakeRegistry(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
    }

    static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
    {
        if (request.Headers.Authorization?.Parameter != Token)
            return Json(HttpStatusCode.Unauthorized, new { code = "TOKEN_INVALID", message = "The token is unknown or revoked." });

        var path = request.RequestUri!.AbsolutePath;
        if (path == "/v1/bundle")
        {
            // What was asked for, as the registry would pack it.
            var packages = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["packages"] ?? "";
            if (packages == "") return Json(HttpStatusCode.BadRequest, new { code = "PACKAGES_REQUIRED", message = "Name the packages." });
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                foreach (var p in packages.Split(','))
                    using (var w = new StreamWriter(zip.CreateEntry($"packages/{p.Replace('@', '-')}.zip").Open())) w.Write("zip");
                using (var w = new StreamWriter(zip.CreateEntry("bundle.json").Open())) w.Write("{}");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
        }
        if (path == "/v1/me") return Json(HttpStatusCode.OK, new { name = "ci", scope = "Publish", publisher = "s9" });
        if (path == "/v1/mine/packages")
            return Json(HttpStatusCode.OK, new[]
            {
                new { id = "s9.lab.pkg", displayName = "Lab", visibility = "Public", pricing = "Free",
                    versions = new object[] { new { version = "1.1.0", status = "InReview" }, new { version = "1.0.0", status = "Revoked", revokeReason = "Leaked a key." } } },
            });
        if (path == "/v1/mine/packages/s9.lab.pkg/versions/1.1.0")
            return Json(HttpStatusCode.OK, new
            {
                version = new { status = "Published" },
                checks = new[] { new { name = "manifest", outcome = "Passed", detail = (string)null } },
                reviewedBy = "reviewer", reviewNotes = "Looks good.",
            });

        if (request.Method == HttpMethod.Post && path.StartsWith("/v1/packages/"))
        {
            var parts = path.Split('/');
            var (id, version) = (parts[3], parts[5]);
            // In process, the request carries the form the CLI built, part by part.
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            HttpContent Part(string name) => form.Single(c => c.Headers.ContentDisposition!.Name!.Trim('"') == name);
            var package = await Part("package").ReadAsByteArrayAsync();
            string Field(string name) => Part(name).ReadAsStringAsync().Result;
            var sha = Convert.ToHexStringLower(SHA256.HashData(package));
            received.Add((id, version, sha, Field("keyId"), Field("signature")));

            if (version == "9.9.9") return Json(HttpStatusCode.Conflict, new { code = "VERSION_EXISTS", message = "That version exists." });
            if (version == "0.0.1")
                return Json(HttpStatusCode.UnprocessableEntity, new
                {
                    packageId = id, version, status = "Rejected", sha256 = sha,
                    checks = new[] { new { name = "manifest", outcome = "Passed", detail = (string)null }, new { name = "source", outcome = "Failed", detail = "No source/ in the package." } },
                });
            return Json(HttpStatusCode.Accepted, new
            {
                packageId = id, version, status = "InReview", sha256 = sha,
                checks = new[] { new { name = "manifest", outcome = "Passed", detail = (string)null }, new { name = "contract", outcome = "Skipped", detail = "Runs in review." } },
            });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    string KeyFile()
    {
        var path = Path.Combine(work, "publisher.pem");
        var der = PrivateKeyInfoFactory.CreatePrivateKeyInfo(key).GetDerEncoded();
        File.WriteAllText(path, "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks) + "\n-----END PRIVATE KEY-----\n");
        return path;
    }

    string Package(string version)
    {
        var path = Path.Combine(work, $"s9.lab.pkg-{version}.zip");
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("adapter.json").Open())) w.Write($$"""{"id":"S9.Lab.Pkg","version":"{{version}}"}""");
        using (var w = new StreamWriter(zip.CreateEntry("main.py").Open())) w.Write("print('hi')\n");
        return path;
    }

    async Task<(int Exit, string Output)> Bitween(string stdin, params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        var synchronized = TextWriter.Synchronized(output);
        Console.SetOut(synchronized);
        try
        {
            var exit = await Program.RunAsync(args, profiles, new StringReader(stdin ?? ""), () => "");
            string text;
            lock (synchronized) text = output.ToString();
            return (exit, string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("{\"@t\""))));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Login_keeps_the_token_and_the_key_s_path_and_publish_signs_what_Bitween_verifies()
    {
        var keyFile = KeyFile();
        var login = await Bitween(Token + "\n", "registry", "login", "https://registry.test/", "--key-file", keyFile);
        Assert.Equal(0, login.Exit);
        Assert.Contains("for the publisher s9", login.Output);
        var keyId = RegistrySignatures.KeyIdOf(key.GeneratePublicKey());
        Assert.Contains($"Signing with the key {keyId}", login.Output);

        // What's kept: the address, the token and the key's path, never the key.
        var kept = profiles.Load().Registry;
        Assert.Equal("https://registry.test", kept.Url);
        Assert.Equal(Token, kept.Token);
        Assert.Equal(keyFile, kept.KeyFile);
        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(profiles.Path));

        var published = await Bitween(null, "registry", "publish", Package("1.1.0"));
        Assert.Equal(0, published.Exit);
        Assert.Contains("s9.lab.pkg 1.1.0 is in review at https://registry.test", published.Output);
        Assert.Contains("skip  contract: Runs in review.", published.Output);

        var sent = received.Single();
        Assert.Equal(("s9.lab.pkg", "1.1.0", keyId), (sent.Id, sent.Version, sent.KeyId));
        Assert.True(RegistrySignatures.Verify(key.GeneratePublicKey(), RegistrySignatures.PublishMessage(sent.Id, sent.Version, sent.Sha256), sent.Signature));

        Assert.Equal(0, (await Bitween(null, "registry", "whoami")).Exit);
    }

    [Fact]
    public async Task A_failed_check_exits_1_with_what_failed_and_a_version_already_there_exits_2()
    {
        await Bitween(Token, "registry", "login", "https://registry.test", "--key-file", KeyFile());

        var refused = await Bitween(null, "registry", "publish", Package("0.0.1"));
        Assert.Equal(1, refused.Exit);
        Assert.Contains("FAIL  source: No source/ in the package.", refused.Output);
        Assert.DoesNotContain("manifest", refused.Output);

        var again = await Bitween(null, "registry", "publish", Package("9.9.9"));
        Assert.Equal(Commands.AlreadyPublished, again.Exit);
        Assert.Contains("already in the registry", again.Output);
    }

    [Fact]
    public async Task Status_lists_the_versions_and_shows_one_s_checks_and_review()
    {
        await Bitween(Token, "registry", "login", "https://registry.test");

        var all = await Bitween(null, "registry", "status", "s9.lab.pkg");
        Assert.Equal(0, all.Exit);
        Assert.Contains("1.1.0        in review", all.Output);
        Assert.Contains("1.0.0        revoked: Leaked a key.", all.Output);

        var one = await Bitween(null, "registry", "status", "s9.lab.pkg", "1.1.0");
        Assert.Contains("s9.lab.pkg 1.1.0 is published.", one.Output);
        Assert.Contains("Reviewed by reviewer: Looks good.", one.Output);
    }

    [Fact]
    public async Task A_bad_token_is_refused_at_login_and_nothing_is_kept_and_publish_needs_a_key()
    {
        var login = await Bitween("bwr_wrong", "registry", "login", "https://registry.test");
        Assert.Equal(1, login.Exit);
        Assert.Contains("unknown or revoked", login.Output);
        Assert.Null(profiles.Load().Registry);

        await Bitween(Token, "registry", "login", "https://registry.test");
        var noKey = await Bitween(null, "registry", "publish", Package("1.2.0"));
        Assert.Equal(1, noKey.Exit);
        Assert.Contains("No signing key", noKey.Output);
        Assert.Empty(received);

        var notAKey = Path.Combine(work, "not-a-key.pem");
        File.WriteAllText(notAKey, "-----BEGIN PRIVATE KEY-----\nbm90IGEga2V5\n-----END PRIVATE KEY-----\n");
        var bad = await Bitween(null, "registry", "publish", Package("1.2.0"), "--key-file", notAKey);
        Assert.Equal(1, bad.Exit);
        Assert.Contains("can't be read", bad.Output);
        Assert.DoesNotContain("bm90IGEga2V5", bad.Output);
    }

    [Fact]
    public async Task Bundle_downloads_the_packages_named_as_one_zip()
    {
        await Bitween(Token, "registry", "login", "https://registry.test");
        var path = Path.Combine(work, "offline", "bundle.zip");
        var (exit, output) = await Bitween(null, "registry", "bundle", "s9.openapi@1.2.0", "s9.soap", "--out", path);
        Assert.Equal(Commands.Success, exit);
        Assert.Contains("2 package(s) from https://registry.test", output);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["packages/s9.openapi-1.2.0.zip", "packages/s9.soap.zip", "bundle.json"], zip.Entries.Select(e => e.FullName).ToArray());

        // Without a package the registry's refusal is said as it said it.
        var (refused, said) = await Bitween(null, "registry", "bundle", " ", "--out", path);
        Assert.Equal(Commands.Failure, refused);
        Assert.Contains("Name at least one package", said);
    }
}
