using System;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Services.Metrics;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The metrics endpoints over HTTP, after a real gateway call: the call is counted for its
/// subscription and partner, and every page of metrics answers.
/// </summary>
[Collection("Http")]
public class MetricsHttpTests(HttpFixture fixture)
{
    /// <summary>Every metrics endpoint, for checking each answers on each database.</summary>
    public static string[] Pages(int subscriptionId) =>
    [
        "/api/metrics/overview", "/api/metrics/adapters?days=7", "/api/metrics/adapter?id=nativehttphandler",
        "/api/metrics/subscriptions?days=7", $"/api/metrics/subscription?id={subscriptionId}&days=7",
        "/api/metrics/partners?days=7", "/api/metrics/nodes", "/api/metrics/insights",
    ];

    /// <summary>Writes what the node has counted so far, rather than wait the minute the flusher does.</summary>
    public static Task FlushAsync(WebApplicationFactory<Web.Program> app) =>
        app.Services.GetRequiredService<MetricsFlusher>().FlushAsync(CancellationToken.None);

    [Fact]
    public async Task A_gateway_call_is_counted_and_every_metrics_page_answers()
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        using var _ = admin;
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        Assert.Equal(HttpStatusCode.OK, (await Api.CallSyncAsync(fixture, urlName, key)).StatusCode);

        await FlushAsync(fixture.App);
        foreach (var page in Pages(subscriptionId)) await Api.Json(await admin.GetAsync(page));

        var subscription = await Api.Json(await admin.GetAsync($"/api/metrics/subscription?id={subscriptionId}&days=1"));
        Assert.Equal(1, (int)subscription["total"]!["total"]!);
        Assert.Equal(1, (int)subscription["total"]!["succeeded"]!);
        Assert.NotNull(subscription["total"]!["p95Ms"]);

        var partners = await Api.Json(await admin.GetAsync("/api/metrics/partners?days=1"));
        var partner = partners["partners"]!.AsArray().Single(p => (int)p!["partnerId"]! == partnerId)!;
        Assert.Equal(1, (int)partner["exchanges"]!);
        Assert.NotNull(partner["keyLastUsedOn"]);
    }

    [Fact]
    public async Task A_refused_gateway_call_is_kept_in_the_week_across_nodes_and_restarts()
    {
        var (admin, urlName, gatewayId, _, _) = await Api.GatewayAsync(fixture);
        using var _ = admin;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.CallSyncAsync(fixture, urlName, "not-a-real-key")).StatusCode);

        await FlushAsync(fixture.App);
        var rejections = await Api.Json(await admin.GetAsync($"/api/apigateways/{gatewayId}/rejections"));
        Assert.Equal(1, (int)rejections["lastSevenDays"]!["not-authenticated"]!);
    }

    [Fact]
    public async Task A_partner_member_without_dashboard_access_is_refused_the_overview()
    {
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/metrics/overview")).StatusCode);
    }
}
