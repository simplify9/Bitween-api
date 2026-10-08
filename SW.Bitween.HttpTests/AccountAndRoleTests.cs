using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Members and roles as an administrator manages them, over HTTP: a custom role grants exactly what
/// it lists, changing the role changes what its members can do, a reset password replaces the old
/// one and ends its sessions, and a role is only removed once nobody holds it.
/// </summary>
[Collection("Http")]
public class AccountAndRoleTests(HttpFixture fixture)
{
    static string Email() => $"member-{Guid.NewGuid():N}@example.com"[..40];

    async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = fixture.Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await HttpFixture.SignInAsync(client, email, password));
        return client;
    }

    static async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(!response.IsSuccessStatusCode, $"expected a refusal, got {(int)response.StatusCode}: {body}");
        return body;
    }

    [Fact]
    public async Task A_custom_role_grants_what_it_lists_and_changing_it_changes_what_its_members_can_do()
    {
        using var admin = await fixture.AdminAsync();
        var roleName = Api.Unique("Type reader");
        var roleId = await Api.CreateAsync(admin, "/api/roles", new
        {
            name = roleName, description = "reads information types", permissions = new[] { "documents.view" }
        });
        var role = await Api.Json(await admin.GetAsync($"/api/roles/{roleId}"));
        Assert.Equal(["documents.view"], role["permissions"]!.AsArray().Select(p => (string)p!).ToArray());

        var email = Email();
        const string password = "Member-Pass-2026!";
        var accountId = await Api.CreateAsync(admin, "/api/accounts", new
        {
            name = "Member One", email, password, roleIds = new[] { roleId }
        });

        // The same email can't be taken twice.
        Assert.Contains("ACCOUNT_EXISTS", await RefusedAsync(await admin.PostAsJsonAsync("/api/accounts", new
        {
            name = "Member Again", email, password, roleIds = new[] { roleId }
        })));

        using var member = await SignedInAsync(email, password);
        Assert.True((await member.GetAsync("/api/documents")).IsSuccessStatusCode);
        var create = new { name = Api.Unique("Invoice"), documentFormat = "Json" };
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/documents", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/partners")).StatusCode);

        // Granted on the role, and the member's existing session can use it.
        await Api.Json(await admin.PostAsJsonAsync($"/api/roles/{roleId}", new
        {
            name = roleName, permissions = new[] { "documents.view", "documents.create" }
        }));
        Assert.True((await member.PostAsJsonAsync("/api/documents", create)).IsSuccessStatusCode);

        // Taken away again, and so is the ability.
        await Api.Json(await admin.PostAsJsonAsync($"/api/roles/{roleId}", new
        {
            name = roleName, permissions = new[] { "documents.view" }
        }));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/documents",
            new { name = Api.Unique("Invoice"), documentFormat = "Json" })).StatusCode);

        // A role somebody holds stays.
        Assert.Contains("ROLE_IN_USE", await RefusedAsync(await admin.DeleteAsync($"/api/roles/{roleId}")));
        await Api.Json(await admin.PostAsJsonAsync($"/api/accounts/{accountId}/setRoles", new { roleIds = Array.Empty<int>() }));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/documents")).StatusCode);
        await Api.Json(await admin.DeleteAsync($"/api/roles/{roleId}"));
        Assert.False((await admin.GetAsync($"/api/roles/{roleId}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_deleted()
    {
        using var admin = await fixture.AdminAsync();
        var roles = (await Api.Json(await admin.GetAsync("/api/roles")))["result"]!.AsArray();
        var builtIn = roles.First(r => (bool)r!["isSystem"]!);
        Assert.Contains("ROLE_IS_BUILT_IN", await RefusedAsync(await admin.DeleteAsync($"/api/roles/{(int)builtIn!["id"]!}")));
    }

    [Fact]
    public async Task A_profile_update_renames_the_member_and_a_password_reset_replaces_the_old_one()
    {
        using var admin = await fixture.AdminAsync();
        var email = Email();
        var accountId = await Api.CreateAsync(admin, "/api/accounts", new
        {
            name = "Before Rename", email, password = "First-Pass-2026!", role = 2
        });

        await Api.Json(await admin.PostAsJsonAsync($"/api/accounts/{accountId}", new { name = "After Rename" }));
        var found = (await Api.Json(await admin.GetAsync("/api/accounts"))).ToJsonString();
        Assert.Contains("After Rename", found);
        Assert.DoesNotContain("Before Rename", found);

        // A session from the old password, with its refresh cookie.
        using var old = fixture.Client();
        var login = await old.PostAsJsonAsync("/api/accounts/login", new { Username = email, Password = "First-Pass-2026!" });
        Assert.True(login.IsSuccessStatusCode);
        var refresh = login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])
            .First(c => c.StartsWith("refresh_token="));

        await Api.Json(await admin.PostAsJsonAsync($"/api/accounts/{accountId}/setPassword", new { password = "Second-Pass-2026!" }));

        using (var again = fixture.Client())
            Assert.False((await again.PostAsJsonAsync("/api/accounts/login",
                new { Username = email, Password = "First-Pass-2026!" })).IsSuccessStatusCode);
        using (var fresh = fixture.Client())
            Assert.True((await fresh.PostAsJsonAsync("/api/accounts/login",
                new { Username = email, Password = "Second-Pass-2026!" })).IsSuccessStatusCode);

        // The reset ends what the old password opened.
        using var tab = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/login")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", refresh);
        Assert.False((await tab.SendAsync(request)).IsSuccessStatusCode, "the old session's refresh token outlived a reset");

        // An administrator resets other people's passwords, not their own.
        var adminId = (int)(await Api.Json(await admin.GetAsync("/api/accounts/profile")))["id"]!;
        Assert.Contains("USE_CHANGE_PASSWORD", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/accounts/{adminId}/setPassword", new { password = "Third-Pass-2026!" })));
    }
}
