using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The deletes that reach furthest, over HTTP: refused plainly while configuration still depends on
/// the thing, and once allowed, taking with them what belongs to it — exchanges, scheduled retries,
/// the broker queue — and nothing else.
/// </summary>
[Collection("Http")]
public class DeleteTests(HttpFixture fixture)
{
    static async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"expected a refusal, got {(int)response.StatusCode}: {body}");
        return body;
    }

    async Task<bool> EventuallyAsync(Func<Task<bool>> condition, int seconds = 30)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (await condition()) return true;
            await Task.Delay(500);
        }
        return await condition();
    }

    [Fact]
    public async Task Deleting_an_information_type_is_refused_while_used_then_takes_its_exchanges_and_queue()
    {
        using var admin = await fixture.AdminAsync();
        var messageType = $"OrderPlaced{Guid.NewGuid():N}"[..24];
        var documentId = await Api.CreateAsync(admin, "/api/documents", new
        {
            name = Api.Unique("Shipment"), documentFormat = "Json", busEnabled = true, busMessageTypeName = messageType
        });
        var lane = $".busservice.{messageType.ToLowerInvariant()}";
        Assert.True(await EventuallyAsync(async () => (await fixture.QueuesAsync()).Any(q => q.EndsWith(lane))),
            $"no queue for {messageType} after enabling the bus");

        var (_, urlName, gatewayId, partnerId, subscriptionId) = await Api.GatewayAsync(fixture, documentId);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));
        var call = await Api.CallSyncAsync(fixture, urlName, key);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Equal(1, await fixture.InDbAsync(db => db.Set<Xchange>().CountAsync(x => x.DocumentId == documentId)));

        Assert.Contains("subscriptions still carry", await RefusedAsync(await admin.DeleteAsync($"/api/documents/{documentId}")));

        await Api.Json(await admin.PostAsJsonAsync($"/api/apigateways/{gatewayId}/removepartner", new { partnerId }));
        await Api.Json(await admin.DeleteAsync($"/api/subscriptions/{subscriptionId}"));
        await Api.Json(await admin.DeleteAsync($"/api/documents/{documentId}"));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/documents/{documentId}")).StatusCode);
        Assert.Equal(0, await fixture.InDbAsync(db => db.Set<Xchange>().CountAsync(x => x.DocumentId == documentId)));
        Assert.True(await EventuallyAsync(async () => !(await fixture.QueuesAsync()).Any(q => q.Contains(lane))),
            $"the queue for {messageType} outlived its information type");
    }

    [Fact]
    public async Task The_built_in_aggregation_type_cannot_be_deleted()
    {
        using var admin = await fixture.AdminAsync();
        Assert.Contains("can't be deleted",
            await RefusedAsync(await admin.DeleteAsync($"/api/documents/{Document.AggregationDocumentId}")));
        Assert.True((await admin.GetAsync($"/api/documents/{Document.AggregationDocumentId}")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Deleting_a_partner_is_refused_while_a_gateway_holds_it_and_the_system_partner_never()
    {
        var (admin, _, gatewayId, partnerId, _) = await Api.GatewayAsync(fixture);
        var gatewayName = (string)(await Api.Json(await admin.GetAsync($"/api/apigateways/{gatewayId}")))["name"]!;

        var refusal = await RefusedAsync(await admin.DeleteAsync($"/api/partners/{partnerId}"));
        Assert.Contains("still used by", refusal);
        Assert.Contains(gatewayName, refusal);

        await Api.Json(await admin.PostAsJsonAsync($"/api/apigateways/{gatewayId}/removepartner", new { partnerId }));
        await Api.Json(await admin.DeleteAsync($"/api/partners/{partnerId}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/partners/{partnerId}")).StatusCode);

        Assert.Contains("can not be deleted", await RefusedAsync(await admin.DeleteAsync($"/api/partners/{Partner.SystemId}")));
        admin.Dispose();
    }
}
