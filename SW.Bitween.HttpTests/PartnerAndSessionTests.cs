using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Partner keys and sessions over real HTTP, as the admin UI and a partner see them: a key issued and
/// saved on a partner lets that partner call its gateway; revoking the key, or detaching the partner,
/// stops it at once. Signing out ends the session for good.
/// </summary>
[Collection("Http")]
public class PartnerAndSessionTests(HttpFixture fixture)
{
    static int _seq;
    static string Unique(string prefix) => $"{prefix} {Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}"[..40];

    static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null! : JsonNode.Parse(text)!;
    }

    static async Task<int> CreateAsync(HttpClient client, string path, object body) =>
        (int)(await Json(await client.PostAsJsonAsync(path, body)));

    /// <summary>Saves the partner's keys the way the partner page does: the whole record, keys replaced.</summary>
    static async Task SetKeysAsync(HttpClient admin, int partnerId, params (string Name, string Value)[] keys)
    {
        var partner = await Json(await admin.GetAsync($"/api/partners/{partnerId}"));
        var save = await admin.PostAsJsonAsync($"/api/partners/{partnerId}", new
        {
            name = (string)partner["name"]!,
            adapterProperties = partner["adapterProperties"],
            secretProperties = partner["secretProperties"],
            loginIdentity = (string?)partner["loginIdentity"],
            apiCredentials = keys.Select(k => new { key = k.Name, value = k.Value })
        });
        await Json(save);
    }

    /// <summary>A gateway with one partner attached through a gateway subscription.</summary>
    async Task<(HttpClient Admin, string UrlName, int GatewayId, int PartnerId, int SubscriptionId)> GatewayAsync()
    {
        var admin = await fixture.AdminAsync();
        var documentId = await CreateAsync(admin, "/api/documents", new { name = Unique("Order"), documentFormat = "Json" });
        var partnerId = await CreateAsync(admin, "/api/partners", new { name = Unique("Partner") });
        var subscriptionId = await CreateAsync(admin, "/api/subscriptions", new
        {
            name = Unique("Gateway sub"), documentId, type = "GatewayApiCall"
        });
        // Created switched off, as every new subscription is; switched on the way the page's
        // Enable does it — the whole record saved back with inactive false.
        var subscription = (await Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
        subscription["inactive"] = false;
        await Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));

        var urlName = $"orders-{Guid.NewGuid():N}"[..20];
        var gatewayId = await CreateAsync(admin, "/api/apigateways", new { name = Unique("Gateway"), urlName, inactive = false });
        await Json(await admin.PostAsJsonAsync($"/api/apigateways/{gatewayId}/addpartner", new { partnerId, subscriptionId }));
        return (admin, urlName, gatewayId, partnerId, subscriptionId);
    }

    /// <summary>A new key, as the partner page asks for one. Answered as plain text.</summary>
    static async Task<string> NewKeyAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/partners/generatekey");
        Assert.True(response.IsSuccessStatusCode);
        return (await response.Content.ReadAsStringAsync()).Trim('"');
    }

    async Task<HttpStatusCode> CallGatewayAsync(string urlName, string key)
    {
        using var partner = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/gateway/{urlName}/async")
        {
            Content = new StringContent("{\"order\":1}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("partnerkey", key);
        return (await partner.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task An_issued_key_lets_the_partner_call_its_gateway_and_revoking_it_stops_it()
    {
        var (admin, urlName, _, partnerId, _) = await GatewayAsync();

        var key = await NewKeyAsync(admin);
        Assert.Matches("^[0-9a-f]{32}$", key);
        Assert.NotEqual(key, await NewKeyAsync(admin));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallGatewayAsync(urlName, key));

        await SetKeysAsync(admin, partnerId, ("orders-prod", key));
        Assert.Equal(HttpStatusCode.Accepted, await CallGatewayAsync(urlName, key));

        // The key is never handed back once saved.
        var stored = (await Json(await admin.GetAsync($"/api/partners/{partnerId}")))["apiCredentials"]!.AsArray();
        Assert.DoesNotContain(stored, c => (string?)c!["value"] == key);

        await SetKeysAsync(admin, partnerId);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallGatewayAsync(urlName, key));
        admin.Dispose();
    }

    [Fact]
    public async Task Detaching_a_partner_from_a_gateway_stops_its_calls()
    {
        var (admin, urlName, gatewayId, partnerId, _) = await GatewayAsync();
        var key = await NewKeyAsync(admin);
        await SetKeysAsync(admin, partnerId, ("main", key));
        Assert.Equal(HttpStatusCode.Accepted, await CallGatewayAsync(urlName, key));

        await Json(await admin.PostAsJsonAsync($"/api/apigateways/{gatewayId}/removepartner", new { partnerId }));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallGatewayAsync(urlName, key));
        admin.Dispose();
    }

    [Fact]
    public async Task A_gateway_call_with_no_key_or_a_wrong_one_is_refused()
    {
        var (admin, urlName, _, partnerId, _) = await GatewayAsync();
        await SetKeysAsync(admin, partnerId, ("main", Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallGatewayAsync(urlName, Guid.NewGuid().ToString("N")));
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/gateway/{urlName}/async", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        admin.Dispose();
    }

    /// <summary>The refresh token a response set, from its HttpOnly cookie — never the body.</summary>
    static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).Where(c => c.StartsWith("refresh_token="))
                .Select(c => c["refresh_token=".Length..]).LastOrDefault(v => v.Length > 0)!
            : null!;

    async Task<HttpResponseMessage> RefreshAsync(string token)
    {
        // A fresh client each time, carrying only this cookie, as a browser tab would.
        using var client = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/login")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", $"refresh_token={token}");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_its_refresh_token()
    {
        using var client = fixture.Client();
        var login = await client.PostAsJsonAsync("/api/accounts/login",
            new { Username = HttpFixture.AdminEmail, Password = HttpFixture.AdminPassword });
        Assert.True(login.IsSuccessStatusCode);
        var token = RefreshCookie(login);
        Assert.False(string.IsNullOrEmpty(token), "sign-in sets the refresh token as a cookie");
        Assert.DoesNotContain(token, await login.Content.ReadAsStringAsync());

        // The cookie opens a new session while signed in, and hands over the next token.
        var refreshed = await RefreshAsync(token);
        Assert.True(refreshed.IsSuccessStatusCode);
        var current = RefreshCookie(refreshed);
        Assert.False(string.IsNullOrEmpty(current));
        Assert.NotEqual(token, current);
        // Superseded, but within its 30-second grace — another tab refreshing at the same moment
        // still gets through.
        Assert.True((await RefreshAsync(token)).IsSuccessStatusCode, "a just-superseded token is honoured in its grace");

        // Signed out from a client holding only the current token — as the browser does: its cookie
        // jar has just the one, the latest the server set.
        using var browser = fixture.Client();
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/logout")
               {
                   Content = new StringContent("{}", Encoding.UTF8, "application/json")
               })
        {
            logout.Headers.Add("Cookie", $"refresh_token={current}");
            var response = await browser.SendAsync(logout);
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("\"cookies\"", string.Join(",", response.Headers.GetValues("Clear-Site-Data")));
        }

        var after = await RefreshAsync(current);
        Assert.False(after.IsSuccessStatusCode,
            $"a signed-out refresh token must not open a new session: {(int)after.StatusCode} {await after.Content.ReadAsStringAsync()}");
    }
}
