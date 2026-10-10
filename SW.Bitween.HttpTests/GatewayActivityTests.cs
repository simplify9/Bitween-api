using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// What a gateway's partners' calls did: the calls turned away and why, and when each key was last
/// used — the two things "the partner says they get 401" needed and had nowhere to look.
/// </summary>
[Collection("Http")]
public class GatewayActivityTests(HttpFixture fixture)
{
    [Fact]
    public async Task Refused_calls_are_counted_by_reason_and_a_used_key_says_when()
    {
        var (admin, urlName, gatewayId, partnerId, _) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("primary", key));

        using (var refused = await Api.CallSyncAsync(fixture, urlName, "not-a-key"))
            Assert.Equal(401, (int)refused.StatusCode);
        using (var accepted = await Api.CallSyncAsync(fixture, urlName, key))
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());

        var rejections = await Api.Json(await admin.GetAsync($"/api/apigateways/{gatewayId}/rejections"));
        Assert.Equal(1, (int)rejections["counts"]!["not-authenticated"]!);
        var recent = rejections["recent"]!.AsArray().Single()!;
        Assert.Equal("not-authenticated", (string)recent["reason"]);
        Assert.Equal(401, (int)recent["status"]!);

        var partner = await Api.Json(await admin.GetAsync($"/api/partners/{partnerId}"));
        Assert.NotNull(partner["keysLastUsedOn"]!["primary"]);
    }
}
