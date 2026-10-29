using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// GET /api/settings/about: what this Bitween is and how it is set up, for whoever may see settings.
/// </summary>
[Collection("Http")]
public class AboutTests(HttpFixture fixture)
{
    [Fact]
    public async Task An_administrator_sees_the_version_node_health_and_runtimes()
    {
        using var admin = await fixture.AdminAsync();
        var about = await Api.Json(await admin.GetAsync("/api/settings/about"));

        Assert.False(string.IsNullOrEmpty((string)about["version"]));
        Assert.Equal(Environment.MachineName, (string)about["node"]!["name"]);
        Assert.Equal("PgSql", (string)about["database"]);

        var health = about["health"]!.AsArray().ToDictionary(h => (string)h!["name"]!, h => (string)h!["status"]!);
        Assert.Equal("Healthy", health["database"]);
        Assert.Equal("Healthy", health["rabbitmq"]);

        var runtimes = about["adapters"]!["runtimes"]!.AsArray().Select(r => (string)r!["name"]).ToArray();
        Assert.Equal(["dotnet", "python", "node"], runtimes);
        Assert.True((bool)about["adapters"]!["runtimes"]![1]!["available"]!, "python3 is needed by these tests, so it is here");
        // The effective value, which the test host raises from the default 600.
        Assert.Equal(100000, (int)about["limits"]!["requestsPerMinute"]!);

        // Never a secret, only whether it is set.
        Assert.DoesNotContain("password", about.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_member_who_cannot_see_settings_is_refused()
    {
        using var admin = await fixture.AdminAsync();
        var email = $"about-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "About-Member-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "About member", email, password, roleIds = new[] { 2 } });

        using var member = fixture.Client();
        member.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            await HttpFixture.SignInAsync(member, email, password));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/settings/about")).StatusCode);
    }
}
