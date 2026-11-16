using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using SW.Bitween.Domain;
using SW.Bitween.Services.Mcp;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// An AI app signing in to /mcp with OAuth, as Claude or ChatGPT does: discovery, the app saying who it
/// is, a member's approval, the code traded for tokens, and the tokens renewed - and each piece refusing
/// what it should.
/// </summary>
[Collection("Http")]
public class OAuthTests(HttpFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => SetEnabledAsync(true);

    public async Task DisposeAsync()
    {
        OAuthClients.HandlerOverride = null;
        await SetEnabledAsync(false);
    }

    async Task SetEnabledAsync(bool enabled)
    {
        using var admin = await fixture.AdminAsync();
        await Api.Json(await admin.PostAsJsonAsync("/api/settings/Bitween.McpEnabled", new { value = enabled ? "true" : "false" }));
        var live = fixture.App.Services.GetRequiredService<BitweenOptions>();
        for (int waited = 0, steady = 0; steady < 300; waited += 50)
        {
            Assert.True(waited < 10_000, "the MCP setting never took effect");
            await Task.Delay(50);
            steady = live.McpEnabled == enabled ? steady + 50 : 0;
        }
    }

    record Pkce(string Verifier, string Challenge)
    {
        public static Pkce New()
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            return new(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    async Task<string> RegisterAsync(string redirectUri, string name = "Test AI app")
    {
        using var http = fixture.Client();
        var registered = await Api.Json(await http.PostAsJsonAsync("/api/oauth/register",
            new Dictionary<string, object> { ["client_name"] = name, ["redirect_uris"] = new[] { redirectUri } }));
        return (string)registered["client_id"]!;
    }

    /// <summary>The member approving on the consent page: the address the page sends them back to.</summary>
    static async Task<Uri> ApproveAsync(HttpClient member, string clientId, string redirectUri, Pkce pkce, bool approve = true)
    {
        var answer = await Api.Json(await member.PostAsJsonAsync("/api/oauth/grant", new
        {
            clientId, redirectUri, codeChallenge = pkce.Challenge, codeChallengeMethod = "S256", state = "xyz", approve,
        }));
        return new Uri((string)answer["redirectTo"]!);
    }

    async Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> form)
    {
        using var http = fixture.Client();
        return await http.PostAsync("/api/oauth/token", new FormUrlEncodedContent(form));
    }

    async Task<JsonNode> CodeForTokensAsync(string clientId, string redirectUri, string code, Pkce pkce) =>
        await Api.Json(await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = pkce.Verifier,
            ["client_id"] = clientId, ["redirect_uri"] = redirectUri,
        }));

    static string Param(Uri uri, string name) =>
        QueryHelpers.ParseQuery(uri.Query).TryGetValue(name, out var value) ? value.ToString() : "";

    Task<McpClient> ConnectAsync(string bearer)
    {
        var http = fixture.Client();
        return McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearer}" },
        }, http, ownsHttpClient: true));
    }

    [Fact]
    public async Task Apps_read_how_to_sign_in_from_the_well_known_addresses()
    {
        using var http = fixture.Client();
        var mcp = new Uri(http.BaseAddress!, "/mcp").ToString();

        var unauthorized = await http.PostAsJsonAsync("/mcp", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Contains("resource_metadata", unauthorized.Headers.WwwAuthenticate.ToString());

        var resource = await Api.Json(await http.GetAsync("/.well-known/oauth-protected-resource/mcp"));
        Assert.Equal(mcp, (string)resource["resource"]!);
        var issuer = (string)resource["authorization_servers"]![0]!;

        var server = await Api.Json(await http.GetAsync("/.well-known/oauth-authorization-server"));
        Assert.Equal(issuer, (string)server["issuer"]!);
        Assert.Equal("S256", (string)server["code_challenge_methods_supported"]![0]!);
        Assert.True((bool)server["client_id_metadata_document_supported"]!);
        Assert.Contains("none", server["token_endpoint_auth_methods_supported"]!.AsArray().Select(m => (string)m!));
        Assert.EndsWith("/oauth/authorize", (string)server["authorization_endpoint"]!);

        await SetEnabledAsync(false);
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-authorization-server")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/.well-known/oauth-protected-resource/mcp")).StatusCode);
        }
        finally
        {
            await SetEnabledAsync(true);
        }
    }

    [Fact]
    public async Task A_registered_app_signs_in_once_per_code_and_renews_only_as_itself()
    {
        const string redirect = "https://app.example.com/callback";
        var clientId = await RegisterAsync(redirect);
        using var member = await fixture.AdminAsync();

        var client = await Api.Json(await member.GetAsync($"/api/oauth/client?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}"));
        Assert.Equal("Test AI app", (string)client["name"]!);
        Assert.Equal("app.example.com", (string)client["redirectHost"]!);

        var pkce = Pkce.New();
        var back = await ApproveAsync(member, clientId, redirect, pkce);
        Assert.StartsWith(redirect, back.ToString());
        Assert.Equal("xyz", Param(back, "state"));
        var code = Param(back, "code");

        var tokens = await CodeForTokensAsync(clientId, redirect, code, pkce);
        Assert.Equal("Bearer", (string)tokens["token_type"]!);
        var access = (string)tokens["access_token"]!;
        var refresh = (string)tokens["refresh_token"]!;

        // Once only.
        var again = await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = pkce.Verifier,
            ["client_id"] = clientId, ["redirect_uri"] = redirect,
        });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("invalid_grant", (string)JsonNode.Parse(await again.Content.ReadAsStringAsync())!["error"]!);

        // The access token works at /mcp, and nowhere else.
        await using (var mcp = await ConnectAsync(access))
            Assert.Contains("list_partners", (await mcp.ListToolsAsync()).Select(t => t.Name));
        using (var api = fixture.Client())
        {
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/partners")).StatusCode);
        }

        // Its refresh token renews it as this app, never as another app or as a browser session.
        var otherClient = await RegisterAsync(redirect, "Another app");
        var asOther = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = otherClient });
        Assert.Equal(HttpStatusCode.BadRequest, asOther.StatusCode);
        using (var browser = fixture.Client())
        {
            var login = await browser.PostAsJsonAsync("/api/accounts/login", new { refreshToken = refresh });
            Assert.NotEqual(HttpStatusCode.OK, login.StatusCode);
        }
        var renewed = await Api.Json(await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = clientId }));
        Assert.NotEqual(refresh, (string)renewed["refresh_token"]!);
        await using (var mcp = await ConnectAsync((string)renewed["access_token"]!))
            Assert.NotEmpty(await mcp.ListToolsAsync());
    }

    [Fact]
    public async Task An_app_known_by_its_metadata_document_comes_back_to_any_port_on_this_computer()
    {
        const string clientId = "https://claude.example.com/oauth/client-metadata.json";
        OAuthClients.HandlerOverride = () => new Documents(new()
        {
            [clientId] = $$"""{"client_id":"{{clientId}}","client_name":"Claude Code","redirect_uris":["http://127.0.0.1/callback","http://localhost/callback"]}""",
        });
        using var member = await fixture.AdminAsync();
        const string redirect = "http://127.0.0.1:53682/callback";

        var client = await Api.Json(await member.GetAsync($"/api/oauth/client?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}"));
        Assert.Equal("Claude Code", (string)client["name"]!);
        Assert.True((bool)client["onlyThisComputer"]!);

        var pkce = Pkce.New();
        var tokens = await CodeForTokensAsync(clientId, redirect, Param(await ApproveAsync(member, clientId, redirect, pkce), "code"), pkce);
        Assert.NotNull(tokens["access_token"]);

        // An address it didn't list, and a document that names another client, are refused.
        var elsewhere = await member.GetAsync($"/api/oauth/client?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString("https://evil.example.com/cb")}");
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        const string impostor = "https://impostor.example.com/meta.json";
        OAuthClients.HandlerOverride = () => new Documents(new()
        {
            [impostor] = $$"""{"client_id":"{{clientId}}","client_name":"Claude Code","redirect_uris":["https://evil.example.com/cb"]}""",
        });
        var named = await member.GetAsync($"/api/oauth/client?client_id={Uri.EscapeDataString(impostor)}&redirect_uri={Uri.EscapeDataString("https://evil.example.com/cb")}");
        Assert.Equal(HttpStatusCode.BadRequest, named.StatusCode);
    }

    [Fact]
    public async Task A_connected_app_is_listed_and_disconnecting_it_ends_its_renewals()
    {
        const string redirect = "https://app.example.com/callback";
        var clientId = await RegisterAsync(redirect, "Listed app");
        using var member = await fixture.AdminAsync();
        var pkce = Pkce.New();
        var tokens = await CodeForTokensAsync(clientId, redirect, Param(await ApproveAsync(member, clientId, redirect, pkce), "code"), pkce);

        var apps = (await Api.Json(await member.GetAsync("/api/accounts/connectedapps"))).AsArray();
        Assert.Contains(apps, a => (string)a!["clientId"]! == clientId && (string)a["name"]! == "Listed app");

        await Api.Json(await member.PostAsJsonAsync("/api/accounts/disconnectapp", new { clientId }));

        apps = (await Api.Json(await member.GetAsync("/api/accounts/connectedapps"))).AsArray();
        Assert.DoesNotContain(apps, a => (string)a!["clientId"]! == clientId);
        var renewal = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = (string)tokens["refresh_token"]!, ["client_id"] = clientId });
        Assert.Equal(HttpStatusCode.BadRequest, renewal.StatusCode);
    }

    [Fact]
    public async Task Declining_sends_the_member_back_with_access_denied_and_no_code()
    {
        const string redirect = "https://app.example.com/callback";
        var clientId = await RegisterAsync(redirect);
        using var member = await fixture.AdminAsync();

        var back = await ApproveAsync(member, clientId, redirect, Pkce.New(), approve: false);

        Assert.Equal("access_denied", Param(back, "error"));
        Assert.Empty(Param(back, "code"));
    }

    [Fact]
    public async Task A_change_made_through_an_app_says_so_in_the_audit_trail()
    {
        const string redirect = "https://app.example.com/callback";
        var clientId = await RegisterAsync(redirect, "Audit app");
        var (admin, _, _, _, subscriptionId) = await Api.GatewayAsync(fixture);
        using var _ = admin;
        var pkce = Pkce.New();
        var tokens = await CodeForTokensAsync(clientId, redirect, Param(await ApproveAsync(admin, clientId, redirect, pkce), "code"), pkce);

        await using (var mcp = await ConnectAsync((string)tokens["access_token"]!))
        {
            var result = await mcp.CallToolAsync("pause_subscription", new Dictionary<string, object?> { ["id"] = subscriptionId });
            Assert.False(result.IsError == true);
        }

        var via = await fixture.InDbAsync(db => db.Set<AuditEntry>()
            .Where(e => e.EntityName == "Subscription" && e.EntityKey == subscriptionId.ToString())
            .OrderByDescending(e => e.OccurredOn).Select(e => e.Via).FirstAsync());
        Assert.Equal("Audit app via MCP", via);
    }

    /// <summary>Metadata documents, by address, in place of the internet.</summary>
    sealed class Documents(Dictionary<string, string> byUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(byUrl.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
