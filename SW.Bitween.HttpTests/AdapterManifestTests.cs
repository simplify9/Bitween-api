using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Client;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// An adapter version's whole manifest over HTTP and through the client, for hosts that configure
/// subscriptions and read the sections Bitween itself does not; and the catalog's typed properties.
/// </summary>
[Collection("Http")]
public class AdapterManifestTests(HttpFixture fixture)
{
    /// <summary>
    /// Written as a newer tool would: a section of the host's own at the top and on a property, which
    /// Bitween has no field for.
    /// </summary>
    static AdapterManifest ManifestOf(string id, string version) => AdapterManifest.Parse($$"""
        {
          "id": "{{id}}",
          "version": "{{version}}",
          "displayName": "Manifest sample {{version}}",
          "kinds": ["handler"],
          "entry": "main.py",
          "publishedOn": "2026-11-16T09:30:00.1234567+00:00",
          "properties": [
            { "name": "Endpoint", "displayName": "Endpoint URL", "type": "text", "required": true, "group": "Connection",
              "x-host-hint": { "placeholder": "https://..." } },
            { "name": "Mode", "type": "select", "options": ["fast", "safe"], "default": "safe" },
            { "name": "Token", "type": "text", "secret": true, "default": "never-shown" }
          ],
          "x-host": { "steps": [{ "name": "send", "dependsOn": [] }], "level": {{(version == "1.0.0" ? 1 : 2)}} }
        }
        """);

    async Task<string> PublishAsync(string? withdrawn = null)
    {
        var id = $"http.handlers.manifest-{Guid.NewGuid():N}"[..38];
        using var scope = fixture.App.Services.CreateScope();
        var files = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
        var root = scope.ServiceProvider.GetRequiredService<ServerlessOptions>().AdapterRemotePath;
        var entry = new AdapterCatalogEntry { Id = id };
        foreach (var version in new[] { "1.0.0", "1.1.0", "1.2.0" })
        {
            var manifest = ManifestOf(id, version);
            entry.Versions.Add(new AdapterVersionRecord
            {
                Version = version, PublishedOn = DateTimeOffset.UtcNow, Manifest = manifest, Withdrawn = version == withdrawn
            });
            if (version == withdrawn) continue;
            entry.Current = version;
            entry.Manifest = manifest;
        }
        await new AdapterCatalogStore(files, root).SaveAsync(entry);
        scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(id);
        return id;
    }

    [Fact]
    public async Task The_current_manifest_comes_back_whole_with_the_hosts_own_sections()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();

        var manifest = await Api.Json(await admin.GetAsync($"/api/adapters/manifest?adapterId={id}"));

        Assert.Equal("1.2.0", (string)manifest["version"]!);
        Assert.Equal("send", (string)manifest["x-host"]!["steps"]![0]!["name"]!);
        Assert.Equal(2, (int)manifest["x-host"]!["level"]!);
        var endpoint = manifest["properties"]![0]!;
        Assert.Equal("Endpoint URL", (string)endpoint["displayName"]!);
        Assert.Equal("https://...", (string)endpoint["x-host-hint"]!["placeholder"]!);
        // Camel-cased as the manifest file is, and a date left as it was written.
        Assert.NotNull(manifest["displayName"]);
        Assert.Null(manifest["DisplayName"]);
        Assert.Equal("2026-11-16T09:30:00.1234567+00:00", (string)manifest["publishedOn"]!);
    }

    [Fact]
    public async Task A_pinned_version_s_manifest_is_that_version_s()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();

        var manifest = await Api.Json(await admin.GetAsync($"/api/adapters/manifest?adapterId={id}&version=1.0.0"));

        Assert.Equal("1.0.0", (string)manifest["version"]!);
        Assert.Equal(1, (int)manifest["x-host"]!["level"]!);
    }

    [Theory]
    [InlineData("1.1.0")] // withdrawn
    [InlineData("9.9.9")] // never published
    public async Task A_version_that_cannot_be_pinned_is_not_found(string version)
    {
        var id = await PublishAsync(withdrawn: "1.1.0");
        using var admin = await fixture.AdminAsync();

        var response = await admin.GetAsync($"/api/adapters/manifest?adapterId={id}&version={version}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_adapter_without_a_catalog_entry_is_not_found()
    {
        using var admin = await fixture.AdminAsync();

        var response = await admin.GetAsync("/api/adapters/manifest?adapterId=http.handlers.nosuchadapter");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_built_in_adapter_s_manifest_is_its_own()
    {
        using var admin = await fixture.AdminAsync();

        var manifest = await Api.Json(await admin.GetAsync("/api/adapters/manifest?adapterId=NativeSmtpHandler"));

        Assert.Equal("Email (SMTP)", (string)manifest["displayName"]!);
    }

    [Fact]
    public async Task The_catalog_lists_the_manifest_s_typed_properties_without_a_secret_s_default()
    {
        var id = await PublishAsync(withdrawn: "1.1.0");
        using var admin = await fixture.AdminAsync();

        var catalog = await Api.Json(await admin.GetAsync("/api/adapters/Catalog?prefix=handlers&startupValues=false"));
        var listed = catalog.AsArray().Single(a => (string)a!["key"]! == id)!;

        var properties = listed["properties"]!.AsArray();
        Assert.Equal(["Endpoint", "Mode", "Token"], properties.Select(p => (string)p!["name"]!));
        Assert.Equal("Endpoint URL", (string)properties[0]!["displayName"]!);
        Assert.Equal("Connection", (string)properties[0]!["group"]!);
        Assert.True((bool)properties[0]!["required"]!);
        Assert.Equal("select", (string)properties[1]!["type"]!);
        Assert.Equal(["fast", "safe"], properties[1]!["options"]!.AsArray().Select(o => (string)o!));
        Assert.Equal("safe", (string)properties[1]!["default"]!);
        Assert.True((bool)properties[2]!["secret"]!);
        Assert.Null((string?)properties[2]!["default"]);

        // Per version too, the withdrawn one included, as the history lists it.
        var history = listed["versionHistory"]!.AsArray();
        Assert.All(history, v => Assert.Equal(3, v!["properties"]!.AsArray().Count));
    }

    [Fact]
    public async Task The_client_reads_a_manifest_whole_and_null_when_there_is_none()
    {
        var id = await PublishAsync(withdrawn: "1.1.0");
        using var admin = await fixture.AdminAsync();
        var roleId = await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("manifest role"), permissions = new[] { "subscriptions.view" } });
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("manifest"), roleIds = new[] { roleId } });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken",
            new { name = "manifest", expiresInDays = 30 }));
        using var bitween = new BitweenClient(fixture.Client(), (string)issued["token"]!);

        var current = (await bitween.Adapters.GetManifestAsync(id))!;
        Assert.Equal("1.2.0", (string)current["version"]!);
        Assert.Equal("send", (string)current["x-host"]!["steps"]![0]!["name"]!);

        var pinned = (await bitween.Adapters.GetManifestAsync(id, "1.0.0"))!;
        Assert.Equal(1, (int)pinned["x-host"]!["level"]!);
        // Its sections parse back into the contract, the host's own kept as extensions.
        Assert.True(AdapterManifest.Parse(pinned.ToJsonString()).Extensions.ContainsKey("x-host"));

        Assert.Null(await bitween.Adapters.GetManifestAsync(id, "1.1.0"));
        Assert.Null(await bitween.Adapters.GetManifestAsync("http.handlers.nosuchadapter"));
    }

    [Fact]
    public async Task Reading_a_manifest_needs_subscriptions_view()
    {
        var id = await PublishAsync();
        using var admin = await fixture.AdminAsync();
        var roleId = await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("no view role"), permissions = new[] { "partners.view" } });
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("noview"), roleIds = new[] { roleId } });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken",
            new { name = "noview", expiresInDays = 30 }));
        using var bitween = new BitweenClient(fixture.Client(), (string)issued["token"]!);

        var refused = await Assert.ThrowsAsync<BitweenClientException>(() => bitween.Adapters.GetManifestAsync(id));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
    }
}
