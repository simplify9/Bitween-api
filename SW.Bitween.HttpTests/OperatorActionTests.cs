using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// What members do beyond configuration, in the audit trail: a sign-in sets the member's last sign-in,
/// and a retry says who retried what.
/// </summary>
[Collection("Http")]
public class OperatorActionTests(HttpFixture fixture)
{
    [Fact]
    public async Task A_sign_in_is_kept_on_the_member_and_a_retry_is_in_the_trail()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);

        var members = await Api.Json(await admin.GetAsync("/api/accounts?limit=500"));
        var me = members["result"]!.AsArray().Single(m => (string)m!["email"] == HttpFixture.AdminEmail)!;
        Assert.True(DateTime.Parse((string)me["lastSignInOn"]!).ToUniversalTime() >= before);

        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("primary", key));
        using (var call = await Api.CallSyncAsync(fixture, urlName, key)) { }
        var exchangeId = await fixture.InDbAsync(db => db.Set<Xchange>().AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId).Select(x => x.Id).FirstAsync());

        var retried = await admin.PostAsJsonAsync($"/api/xchanges/{exchangeId}/retry", new { reason = "test", reset = false });
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());

        var trail = await Api.Json(await admin.GetAsync("/api/audit?entityName=OperatorAction&limit=50"));
        Assert.Contains(trail["result"]!.AsArray(), r =>
            (string?)r!["changes"]?["Action"]?["new"] == "retry" && (string?)r["changes"]?["Target"]?["new"] == exchangeId);
    }
}
