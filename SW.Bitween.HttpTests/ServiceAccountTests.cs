using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Service accounts over HTTP: a token is shown once, traded for a session that can do what the
/// account's roles allow and no more, and stops working when it's revoked, expires, or its account
/// is disabled or removed. Service accounts are kept apart from members in both directions.
/// </summary>
[Collection("Http")]
public class ServiceAccountTests(HttpFixture fixture)
{
    static async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(!response.IsSuccessStatusCode, $"expected a refusal, got {(int)response.StatusCode}: {body}");
        return body;
    }

    Task<HttpResponseMessage> TradeAsync(string token) =>
        fixture.Client().PostAsJsonAsync("/api/accounts/servicetoken", new { token });

    async Task<HttpClient> SessionAsync(string token)
    {
        var answer = await Api.Json(await TradeAsync(token));
        var client = fixture.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)answer["jwt"]!);
        return client;
    }

    static async Task<(int Id, string Token, int TokenId)> ServiceAccountAsync(HttpClient admin, params int[] roleIds)
    {
        var id = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("service account"), roleIds });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/issueToken",
            new { name = "first", expiresInDays = 30 }));
        return (id, (string)issued["token"]!, (int)issued["id"]!);
    }

    static async Task<int> RoleAsync(HttpClient admin, params string[] permissions) =>
        await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("service role"), permissions });

    static async Task<JsonNode> ListedAsync(HttpClient admin, int id) =>
        (await Api.Json(await admin.GetAsync("/api/serviceaccounts"))).AsArray().Single(a => (int)a!["id"]! == id)!;

    [Fact]
    public async Task A_token_is_shown_once_and_its_session_can_do_what_the_roles_allow_and_no_more()
    {
        using var admin = await fixture.AdminAsync();
        var (id, token, _) = await ServiceAccountAsync(admin, await RoleAsync(admin, "documents.view"));

        Assert.StartsWith("bwt_", token);
        Assert.Equal(44, token.Length);
        // Never again: not in the list, and not in the account's history either.
        var listed = await ListedAsync(admin, id);
        Assert.DoesNotContain(token, listed.ToJsonString());
        Assert.Equal(token[..12], (string)listed["tokens"]![0]!["shown"]!);
        var stored = await fixture.InDbAsync(db => db.Set<ServiceToken>().AsNoTracking().SingleAsync(t => t.AccountId == id));
        Assert.NotEqual(token, stored.Hash);

        using var session = await SessionAsync(token);
        Assert.True((await session.GetAsync("/api/documents")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await session.PostAsJsonAsync("/api/documents",
            new { name = Api.Unique("Invoice"), documentFormat = "Json" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/partners")).StatusCode);

        // The use is recorded on the token and the account.
        listed = await ListedAsync(admin, id);
        Assert.NotNull((string?)listed["lastSignInOn"]);
        Assert.NotNull((string?)listed["tokens"]![0]!["lastUsedOn"]);
    }

    [Fact]
    public async Task A_token_stops_working_when_revoked_expired_or_its_account_is_disabled_or_removed()
    {
        using var admin = await fixture.AdminAsync();
        var (id, first, firstId) = await ServiceAccountAsync(admin, await RoleAsync(admin, "documents.view"));

        // Two at a time, so one can replace the other; a third is refused until one goes.
        var second = (string)(await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/issueToken",
            new { name = "second" })))["token"]!;
        Assert.Contains("TOO_MANY_TOKENS", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/serviceaccounts/{id}/issueToken", new { name = "third" })));
        Assert.Contains("ExpiresInDays", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/serviceaccounts/{id}/issueToken", new { name = "too long", expiresInDays = 400 })));

        await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/revokeToken", new { tokenId = firstId }));
        Assert.Contains("isn't valid", await RefusedAsync(await TradeAsync(first)));
        Assert.Equal("revoked", (string)(await ListedAsync(admin, id))["tokens"]!.AsArray()
            .Single(t => (int)t!["id"]! == firstId)!["status"]!);

        // Disabling ends sessions already open, not only new ones.
        using var session = await SessionAsync(second);
        await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/setDisabled", new { disabled = true }));
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/documents")).StatusCode);
        await RefusedAsync(await TradeAsync(second));
        await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/setDisabled", new { disabled = false }));
        (await SessionAsync(second)).Dispose();

        // Expired, by the clock rather than by anyone.
        await fixture.InDbAsync(db => db.Set<ServiceToken>().Where(t => t.AccountId == id && t.RevokedOn == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.ExpiresOn, DateTime.UtcNow.AddMinutes(-1))));
        await RefusedAsync(await TradeAsync(second));
        Assert.Equal("expired", (string)(await ListedAsync(admin, id))["tokens"]![0]!["status"]!);

        // Removed: gone from the list, its tokens revoked with it.
        var third = (string)(await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{id}/issueToken",
            new { name = "last" })))["token"]!;
        (await SessionAsync(third)).Dispose();
        await Api.Json(await admin.DeleteAsync($"/api/serviceaccounts/{id}"));
        await RefusedAsync(await TradeAsync(third));
        Assert.DoesNotContain((await Api.Json(await admin.GetAsync("/api/serviceaccounts"))).AsArray(), a => (int)a!["id"]! == id);
    }

    [Fact]
    public async Task Service_accounts_are_not_members_and_members_are_not_service_accounts()
    {
        using var admin = await fixture.AdminAsync();
        var (id, token, _) = await ServiceAccountAsync(admin, await RoleAsync(admin, "documents.view"));

        // Not in the member list, and out of reach of the member endpoints...
        var members = (await Api.Json(await admin.GetAsync("/api/accounts?limit=500")))["result"]!.AsArray();
        Assert.DoesNotContain(members, m => (int)m!["id"]! == id);
        Assert.Contains("ACCOUNT_NOT_FOUND", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/accounts/{id}/setPassword", new { password = "Some-Password-2026!" })));
        Assert.Contains("ACCOUNT_NOT_FOUND", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/accounts/{id}/setRoles", new { roleIds = new[] { 1 } })));
        // ...but still named where its changes are shown.
        var lookup = await Api.Json(await admin.GetAsync("/api/accounts?lookup=true"));
        Assert.NotNull(lookup[id.ToString()]);

        // A session from a token can't make itself a password or a browser sign-in.
        using var session = await SessionAsync(token);
        await RefusedAsync(await session.PostAsJsonAsync("/api/accounts/changepassword",
            new { oldPassword = "", newPassword = "Some-Password-2026!" }));

        // And a member's id isn't a service account's.
        var memberId = members.Select(m => (int)m!["id"]!).First();
        Assert.Contains("SERVICE_ACCOUNT_NOT_FOUND", await RefusedAsync(await admin.PostAsJsonAsync(
            $"/api/serviceaccounts/{memberId}/issueToken", new { name = "sneaky" })));
    }

    [Fact]
    public async Task Nobody_makes_a_service_account_stronger_than_themselves()
    {
        using var admin = await fixture.AdminAsync();
        var roleId = await RoleAsync(admin, "documents.view", "service-accounts.view", "service-accounts.create");
        var email = $"svc-maker-{Guid.NewGuid():N}@example.com"[..40];
        const string password = "Member-Pass-2026!";
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Maker", email, password, roleIds = new[] { roleId } });
        using var maker = fixture.Client();
        maker.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await HttpFixture.SignInAsync(maker, email, password));

        Assert.Contains("BEYOND_YOUR_PERMISSIONS", await RefusedAsync(await maker.PostAsJsonAsync("/api/serviceaccounts",
            new { name = Api.Unique("service account"), roleIds = new[] { Role.AdministratorId } })));

        // Nor a token for an account that can do more than they can.
        var (strongId, _, _) = await ServiceAccountAsync(admin, Role.AdministratorId);
        Assert.Contains("ACCOUNT_OUTRANKS_YOU", await RefusedAsync(await maker.PostAsJsonAsync(
            $"/api/serviceaccounts/{strongId}/issueToken", new { name = "mine now" })));

        // Without the grant at all, there's nothing to see.
        var (_, token, _) = await ServiceAccountAsync(admin, await RoleAsync(admin, "documents.view"));
        using var session = await SessionAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/serviceaccounts")).StatusCode);
    }

    [Fact]
    public async Task Something_that_is_not_a_token_is_refused_the_same_way()
    {
        Assert.Contains("isn't valid", await RefusedAsync(await TradeAsync("bwt_" + new string('x', 40))));
        Assert.Contains("isn't valid", await RefusedAsync(await TradeAsync("not even close")));
    }
}
