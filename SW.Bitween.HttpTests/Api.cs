using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>Setting things up the way the admin UI does, over the API.</summary>
static class Api
{
    static int _seq;
    public static string Unique(string prefix) => $"{prefix} {Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}"[..40];

    public static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null! : JsonNode.Parse(text)!;
    }

    public static async Task<int> CreateAsync(HttpClient client, string path, object body) =>
        (int)(await Json(await client.PostAsJsonAsync(path, body)));

    /// <summary>Saves the partner's keys the way the partner page does: the whole record, keys replaced.</summary>
    public static async Task SetKeysAsync(HttpClient admin, int partnerId, params (string Name, string Value)[] keys)
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
    public static async Task<(HttpClient Admin, string UrlName, int GatewayId, int PartnerId, int SubscriptionId)> GatewayAsync(HttpFixture fixture)
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
    public static async Task<string> NewKeyAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/partners/generatekey");
        Assert.True(response.IsSuccessStatusCode);
        return (await response.Content.ReadAsStringAsync()).Trim('"');
    }

}
