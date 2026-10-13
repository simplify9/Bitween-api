using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>Every Connections route through the real routing, as the admin UI and bitween apply call them.</summary>
[Collection("Http")]
public class ConnectionHttpTests(HttpFixture fixture)
{
    static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task A_connection_is_created_listed_read_changed_tested_and_deleted_over_http()
    {
        using var admin = await fixture.AdminAsync();
        var name = $"http{Guid.NewGuid():N}"[..20];
        var save = new
        {
            name, grant = "ClientCredentials", tokenUrl = "https://127.0.0.1:9/token", clientId = "app", clientSecret = "s3cret",
        };

        var created = await Json(await admin.PostAsJsonAsync("/api/connections", save));
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal("__private__", created.GetProperty("clientSecret").GetString());

        var listed = await Json(await admin.GetAsync("/api/connections"));
        Assert.Contains(listed.GetProperty("result").EnumerateArray(), c => c.GetProperty("id").GetInt32() == id);

        var read = await Json(await admin.GetAsync($"/api/connections/{id}"));
        Assert.Equal($"{{{{connections.{name}.accessToken}}}}", read.GetProperty("reference").GetString());

        var changed = await Json(await admin.PostAsJsonAsync($"/api/connections/{id}", save));
        Assert.Equal(id, changed.GetProperty("id").GetInt32());

        // Nothing answers at that address, so the test says so rather than failing the call.
        var tested = await Json(await admin.PostAsJsonAsync($"/api/connections/{id}/test", new { }));
        Assert.Equal("Error", tested.GetProperty("status").GetString());

        // Authorize is only for an approve-once connection.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/connections/{id}/authorize", new { })).StatusCode);

        Assert.True((await admin.DeleteAsync($"/api/connections/{id}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/connections/{id}")).StatusCode);
    }

    [Fact]
    public async Task Without_a_session_nothing_is_open()
    {
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/connections/complete", new { code = "c", state = "s" })).StatusCode);
    }

    /// <summary>The bookmark routes the subscription page calls: read it, and forget it.</summary>
    [Fact]
    public async Task A_subscription_s_bookmark_is_read_and_forgotten_over_http()
    {
        using var admin = await fixture.AdminAsync();
        var (_, _, _, _, id) = await Api.GatewayAsync(admin);

        var bookmark = await Json(await admin.GetAsync($"/api/subscriptions/bookmark?subscriptionId={id}"));
        Assert.Equal(JsonValueKind.Null, bookmark.GetProperty("value").ValueKind);

        var forgotten = await Json(await admin.PostAsJsonAsync($"/api/subscriptions/{id}/forgetbookmark", new { }));
        Assert.False(forgotten.GetProperty("forgotten").GetBoolean());
    }
}
