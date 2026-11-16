using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Bitween's MCP server at /mcp, driven by the MCP SDK's own client as an AI app would: who may
/// connect, what each tool answers, and that it answers as the API does.
/// </summary>
[Collection("Http")]
public class McpTests(HttpFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => SetEnabledAsync(true);

    public Task DisposeAsync() => SetEnabledAsync(false);

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

    /// <summary>A token of a new service account whose one role has <paramref name="permissions"/>.</summary>
    async Task<string> TokenAsync(params string[] permissions)
    {
        using var admin = await fixture.AdminAsync();
        var roleId = await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("MCP test role"), permissions });
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("MCP test account"), roleIds = new[] { roleId } });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken",
            new { name = "mcp", expiresInDays = 30 }));
        return (string)issued["token"]!;
    }

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

    static async Task<JsonNode> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? new Dictionary<string, object?>());
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.False(result.IsError == true, text);
        return JsonNode.Parse(text)!;
    }

    static async Task<string> ErrorAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? new Dictionary<string, object?>());
        Assert.True(result.IsError);
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    /// <summary>A gateway whose partner has made one exchange with <paramref name="body"/>.</summary>
    async Task<(int PartnerId, int SubscriptionId)> ExchangeAsync(string body)
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        using var _ = admin;
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("primary", key));
        using (var answer = await Api.CallSyncAsync(fixture, urlName, key, body))
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return (partnerId, subscriptionId);
    }

    [Fact]
    public async Task Off_in_Settings_the_endpoint_is_not_found()
    {
        var token = await TokenAsync("exchanges.view");
        await SetEnabledAsync(false);
        try
        {
            using var http = fixture.Client();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(request)).StatusCode);
        }
        finally
        {
            await SetEnabledAsync(true);
        }
    }

    [Fact]
    public async Task A_service_token_searches_and_reads_exchanges_without_file_links()
    {
        var body = $"{{\"order\":\"{Guid.NewGuid():N}\"}}";
        var (partnerId, _) = await ExchangeAsync(body);
        await using var client = await ConnectAsync(await TokenAsync("exchanges.view"));

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("search_exchanges", tools);
        Assert.Contains("retry_exchange", tools);
        Assert.True((await client.ListToolsAsync()).Single(t => t.Name == "search_exchanges").ProtocolTool.Annotations?.ReadOnlyHint);

        var page = await CallAsync(client, "search_exchanges", new() { ["partnerIds"] = new[] { partnerId }, ["status"] = "success" });
        Assert.Equal(1, (int)page["totalCount"]!);
        var row = page["result"]![0]!;
        Assert.Null(row["inputUrl"]);
        var id = (string)row["id"]!;

        var exchange = await CallAsync(client, "get_exchange", new() { ["id"] = id });
        Assert.Equal(id, (string)exchange["exchange"]!["id"]!);
        Assert.NotNull(exchange["retryChain"]);

        var file = await CallAsync(client, "read_exchange_file", new() { ["id"] = id, ["file"] = "input" });
        Assert.Equal(body, (string)file["text"]!);
        Assert.False((bool)file["truncated"]!);
        var cut = await CallAsync(client, "read_exchange_file", new() { ["id"] = id, ["file"] = "input", ["maxCharacters"] = 5 });
        Assert.Equal(body[..5], (string)cut["text"]!);
        Assert.True((bool)cut["truncated"]!);
    }

    [Fact]
    public async Task Retrying_an_exchange_answers_with_the_new_attempt()
    {
        var (partnerId, _) = await ExchangeAsync("{\"order\":2}");
        await using var client = await ConnectAsync(await TokenAsync("exchanges.view", "exchanges.operate"));
        var id = (string)(await CallAsync(client, "search_exchanges", new() { ["partnerIds"] = new[] { partnerId } }))["result"]![0]!["id"]!;

        var retried = await CallAsync(client, "retry_exchange", new() { ["id"] = id, ["reason"] = "MCP test" });

        Assert.Equal(id, (string)retried["retried"]!);
        Assert.NotEqual(id, (string)retried["newAttempt"]!);
        Assert.Contains("already", (await ErrorAsync(client, "retry_exchange", new() { ["id"] = id })).ToLowerInvariant());
    }

    [Fact]
    public async Task The_dashboard_and_queue_health_answer_part_by_part()
    {
        await using var client = await ConnectAsync(await TokenAsync("dashboard.view", "monitoring.view"));

        var dashboard = await CallAsync(client, "dashboard");
        Assert.NotNull(dashboard["traffic"]);
        Assert.NotNull(dashboard["failuresToActOn"]);
        var queues = await CallAsync(client, "queue_health");
        Assert.NotNull(queues["summary"]);
    }

    [Fact]
    public async Task A_tool_the_role_doesnt_allow_says_so()
    {
        await using var client = await ConnectAsync(await TokenAsync("partners.view"));

        Assert.Contains("role doesn't allow", await ErrorAsync(client, "search_exchanges"));
        Assert.NotNull((await CallAsync(client, "list_partners"))["result"]);
    }

    [Fact]
    public async Task Pausing_and_resuming_say_whether_anything_changed()
    {
        var (_, subscriptionId) = await ExchangeAsync("{\"order\":1}");
        await using var client = await ConnectAsync(await TokenAsync("subscriptions.view", "subscriptions.operate"));

        Assert.True((bool)(await CallAsync(client, "pause_subscription", new() { ["id"] = subscriptionId }))["changed"]!);
        Assert.False((bool)(await CallAsync(client, "pause_subscription", new() { ["id"] = subscriptionId }))["changed"]!);
        Assert.NotNull((await CallAsync(client, "get_subscription", new() { ["id"] = subscriptionId }))["pausedOn"]);
        Assert.True((bool)(await CallAsync(client, "resume_subscription", new() { ["id"] = subscriptionId }))["changed"]!);
    }

    [Fact]
    public async Task Retrying_many_needs_a_filter()
    {
        await using var client = await ConnectAsync(await TokenAsync("exchanges.view", "exchanges.operate"));

        Assert.Contains("at least one filter", await ErrorAsync(client, "retry_exchanges"));
    }

    [Fact]
    public async Task An_API_session_and_an_unknown_token_are_refused()
    {
        using var admin = await fixture.AdminAsync();
        var session = admin.DefaultRequestHeaders.Authorization!.Parameter!;

        foreach (var bearer in new[] { session, "bwt_" + new string('x', 40) })
        {
            using var http = fixture.Client();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(request)).StatusCode);
        }
    }

    [Fact]
    public async Task Well_known_addresses_nothing_serves_are_not_found()
    {
        using var http = fixture.Client();
        var answer = await http.GetAsync("/.well-known/something-else");
        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }
}
