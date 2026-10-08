using System;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// What one instance changes, another instance on the same database and broker picks up — by the
/// cache-revoke broadcast over RabbitMQ, not by a restart. And saving a subscription's mapper.
/// </summary>
[Collection("Http")]
public class ClusterAndMapperTests(HttpFixture fixture)
{
    [Fact]
    public async Task A_setting_changed_on_one_node_reaches_the_other_without_a_restart()
    {
        await using var second = fixture.SecondNode();
        using var onSecond = HttpFixture.ClientOf(second);
        var before = (string?)(await Api.Json(await onSecond.GetAsync("/api/settings/config")))["theme"]!["companyName"];

        var name = $"Acme {Guid.NewGuid():N}"[..20];
        using var admin = await fixture.AdminAsync();
        await Api.Json(await admin.PostAsJsonAsync("/api/settings/Theme.CompanyName", new { value = name }));

        string? seen = before;
        var clock = Stopwatch.StartNew();
        while (seen != name && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(250);
            seen = (string?)(await Api.Json(await onSecond.GetAsync("/api/settings/config")))["theme"]!["companyName"];
        }
        Assert.Equal(name, seen);
    }

    [Fact]
    public async Task A_subscription_switched_off_on_one_node_stops_taking_calls_on_the_other()
    {
        await using var second = fixture.SecondNode();
        using var onSecond = HttpFixture.ClientOf(second);
        onSecond.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await HttpFixture.SignInAsync(onSecond, HttpFixture.AdminEmail, HttpFixture.AdminPassword));

        using var admin = await fixture.AdminAsync();
        var (_, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));

        // The second node learns of the gateway, and caches it, by serving a call.
        using (var partner = HttpFixture.ClientOf(second))
        {
            partner.DefaultRequestHeaders.Add("partnerkey", key);
            Assert.Equal(System.Net.HttpStatusCode.Accepted,
                (await partner.PostAsync($"/api/gateway/{urlName}/async", JsonContent.Create(new { n = 1 }))).StatusCode);
        }

        // Switched off on the first node: the second must stop accepting calls for it.
        var subscription = (await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
        subscription["inactive"] = true;
        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));

        var status = System.Net.HttpStatusCode.Accepted;
        var clock = Stopwatch.StartNew();
        while (status == System.Net.HttpStatusCode.Accepted && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(250);
            using var partner = HttpFixture.ClientOf(second);
            partner.DefaultRequestHeaders.Add("partnerkey", key);
            status = (await partner.PostAsync($"/api/gateway/{urlName}/async", JsonContent.Create(new { n = 2 }))).StatusCode;
        }
        Assert.NotEqual(System.Net.HttpStatusCode.Accepted, status);
    }

    [Fact]
    public async Task Saving_a_mapper_stores_it_on_the_subscription()
    {
        var (admin, _, _, _, subscriptionId) = await Api.GatewayAsync(fixture);

        await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}/savemapper", new
        {
            mapperId = "NativeMapper",
            mapperProperties = new[] { new { key = "Note", value = "kept" } }
        }));

        var saved = await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"));
        Assert.Equal("NativeMapper", (string?)saved["mapperId"]);
        Assert.Contains("kept", saved["mapperProperties"]!.ToJsonString());

        var missing = await admin.PostAsJsonAsync("/api/subscriptions/999999/savemapper", new
        {
            mapperId = "NativeMapper", mapperProperties = Array.Empty<object>()
        });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        admin.Dispose();
    }
}
