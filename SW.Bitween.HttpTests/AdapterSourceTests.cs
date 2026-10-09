using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// An adapter version's source over HTTP: the list and a file at their routes, the read in the
/// audit trail under the reader's name, and the permission — refused to Members, grantable to a
/// custom role.
/// </summary>
[Collection("Http")]
public class AdapterSourceTests(HttpFixture fixture)
{
    const string Handler = "def handle(xchange):\n    return xchange\n";

    async Task<string> PublishAsync()
    {
        var id = $"http.handlers.source-{Guid.NewGuid():N}"[..36];
        var bytes = Encoding.UTF8.GetBytes(Handler);
        var manifest = new AdapterManifest
        {
            Id = id, Version = "1.0.0", Kinds = ["handler"], Entry = "main.py", Language = "python", Runtime = AdapterManifest.PythonRuntime,
            Source = new AdapterSource
            {
                BuildCommand = "serverless build",
                Files = { ["adapter/main.py"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() }
            }
        };

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var s = archive.CreateEntry(AdapterManifest.FileName).Open()) await s.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJson()));
            await using (var s = archive.CreateEntry("main.py").Open()) await s.WriteAsync(bytes);
            await using (var s = archive.CreateEntry("source/adapter/main.py").Open()) await s.WriteAsync(bytes);
        }

        using var scope = fixture.App.Services.CreateScope();
        var files = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
        var root = scope.ServiceProvider.GetRequiredService<ServerlessOptions>().AdapterRemotePath;
        zip.Position = 0;
        await files.WriteAsync(zip, new WriteFileSettings { Key = AdapterCatalogPaths.Version(root, id, "1.0.0"), ContentType = "application/zip" });
        await new AdapterCatalogStore(files, root).SaveAsync(new AdapterCatalogEntry
        {
            Id = id, Current = "1.0.0", Manifest = manifest,
            Versions = [new AdapterVersionRecord { Version = "1.0.0", PublishedOn = DateTimeOffset.UtcNow, Manifest = manifest }]
        });
        scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(id);
        return id;
    }

    async Task<HttpClient> MemberWithRolesAsync(HttpClient admin, params int[] roleIds)
    {
        var email = $"src-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Source-Reader-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Source reader", email, password, roleIds });
        var client = fixture.Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await HttpFixture.SignInAsync(client, email, password));
        return client;
    }

    [Fact]
    public async Task An_admin_lists_and_reads_a_version_s_source_and_the_read_is_audited()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();

        var listing = await Api.Json(await admin.GetAsync($"/api/adapters/source?adapterId={id}&version=1.0.0"));
        Assert.Equal("python", (string)listing["language"]!);
        Assert.Equal(["adapter/main.py"], listing["files"]!.AsArray().Select(f => (string)f!["path"]!).ToArray());

        var file = await Api.Json(await admin.GetAsync($"/api/adapters/sourcefile?adapterId={id}&version=1.0.0&path={Uri.EscapeDataString("adapter/main.py")}"));
        Assert.Equal(Handler, (string)file["content"]!);
        Assert.False((bool)file["binary"]!);

        // The catalog says which versions carry source, for the client to offer it.
        var catalog = await Api.Json(await admin.GetAsync("/api/adapters/Catalog?prefix=handlers"));
        var row = catalog.AsArray().Single(a => (string)a!["key"]! == id)!;
        Assert.True((bool)row["versionHistory"]![0]!["hasSource"]!);

        var trail = await Api.Json(await admin.GetAsync("/api/audit?entityName=AdapterSourceAccess&limit=200"));
        Assert.Contains(trail["result"]!.AsArray(), r =>
            (string?)r!["changes"]?["AdapterId"]?["new"] == id &&
            (string?)r["changes"]?["Path"]?["new"] == "adapter/main.py" &&
            !string.IsNullOrEmpty((string?)r["userDisplayName"]));
    }

    [Fact]
    public async Task Unknown_versions_and_files_are_not_found()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();

        Assert.Equal(404, (int)(await admin.GetAsync($"/api/adapters/source?adapterId={id}&version=9.9.9")).StatusCode);
        Assert.Equal(404, (int)(await admin.GetAsync($"/api/adapters/sourcefile?adapterId={id}&version=1.0.0&path=main.py")).StatusCode);
        Assert.Equal(404, (int)(await admin.GetAsync($"/api/adapters/sourcefile?adapterId={id}&version=1.0.0&path=..%2Fmain.py")).StatusCode);
    }

    [Fact]
    public async Task Members_are_refused_and_a_custom_role_can_be_granted_it()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();

        using var member = await MemberWithRolesAsync(admin, 2);
        Assert.Equal(403, (int)(await member.GetAsync($"/api/adapters/source?adapterId={id}&version=1.0.0")).StatusCode);

        var roleId = await Api.CreateAsync(admin, "/api/roles", new
        {
            name = Api.Unique("Source reader"), description = "reads adapter source", permissions = new[] { "adapter-source.view" }
        });
        using var reader = await MemberWithRolesAsync(admin, roleId);
        var listing = await Api.Json(await reader.GetAsync($"/api/adapters/source?adapterId={id}&version=1.0.0"));
        Assert.Single(listing["files"]!.AsArray());
    }
}
