using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The adapter editor over HTTP, as the UI calls it: start a Python adapter, check it with a setting,
/// try it, publish it and make it current — and a Member refused all of it.
/// </summary>
[Collection("Http")]
public class AdapterEditorTests(HttpFixture fixture)
{
    [Fact]
    public async Task An_admin_writes_checks_tries_publishes_and_promotes_a_python_adapter()
    {
        using var admin = await fixture.AdminAsync();
        var name = "HttpPy" + Guid.NewGuid().ToString("N")[..8];
        var id = (int)await Api.Json(await admin.PostAsJsonAsync("/api/adapterdrafts", new { name, language = "python", kind = "handler" }));

        var draft = await Api.Json(await admin.GetAsync($"/api/adapterdrafts/{id}"));
        var adapterId = (string)draft["adapterId"]!;
        Assert.NotNull(draft["files"]!["main.py"]);

        var settings = new { ApiKey = "k" };
        var built = await Api.Json(await admin.PostAsJsonAsync($"/api/adapterdrafts/{id}/build", new { settings }));
        Assert.True((bool)built["conforms"]!, built.ToJsonString());

        var tried = await Api.Json(await admin.PostAsJsonAsync($"/api/adapterdrafts/{id}/try",
            new { settings, command = "Handle", input = "{\"Data\":\"ping\"}" }));
        Assert.True((bool)tried["succeeded"]!, tried.ToJsonString());
        Assert.Contains("ping", (string)tried["output"]!);

        var published = await Api.Json(await admin.PostAsJsonAsync($"/api/adapterdrafts/{id}/publish",
            new { version = "1.0.0", releaseNotes = "From HTTP", settings }));
        Assert.True((bool)published["published"]!, published.ToJsonString());

        await Api.Json(await admin.PostAsJsonAsync("/api/adapters/promote", new { adapterId, version = "1.0.0" }));
        var catalog = await Api.Json(await admin.GetAsync("/api/adapters/Catalog?prefix=handlers"));
        var row = catalog.AsArray().Single(a => (string)a!["key"]! == adapterId)!;
        Assert.Equal("1.0.0", (string)row["currentVersion"]!);
        Assert.Equal("python", (string)row["versionHistory"]![0]!["runtime"]!);

        var trail = await Api.Json(await admin.GetAsync("/api/audit?entityName=AdapterRelease&limit=200"));
        Assert.Contains(trail["result"]!.AsArray(), r =>
            (string?)r!["changes"]?["AdapterId"]?["new"] == adapterId && (string?)r["changes"]?["Action"]?["new"] == "promoted");
    }

    [Fact]
    public async Task A_member_is_refused_the_editor()
    {
        using var admin = await fixture.AdminAsync();
        var email = $"ed-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Editor-Refused-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Member", email, password, roleIds = new[] { 2 } });
        using var member = fixture.Client();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await HttpFixture.SignInAsync(member, email, password));

        Assert.Equal(403, (int)(await member.GetAsync("/api/adapterdrafts")).StatusCode);
        Assert.Equal(403, (int)(await member.PostAsJsonAsync("/api/adapterdrafts", new { name = "Nope", language = "python", kind = "handler" })).StatusCode);
        Assert.Equal(403, (int)(await member.PostAsJsonAsync("/api/adapters/promote", new { adapterId = "x", version = "1.0.0" })).StatusCode);
    }
}
