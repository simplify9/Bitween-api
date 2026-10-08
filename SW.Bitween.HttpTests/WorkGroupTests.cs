using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// A work group is its own pair of lanes on the broker. Over HTTP and against the real broker: its
/// subscriptions are processed through its lane, renaming its bus name moves the lane, and deleting
/// it — refused while a subscription is in it — takes the lanes away.
/// </summary>
[Collection("Http")]
public class WorkGroupTests(HttpFixture fixture)
{
    async Task<bool> EventuallyAsync(Func<string[], bool> condition, int seconds = 30)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition(await fixture.QueuesAsync())) return true;
            await Task.Delay(500);
        }
        return condition(await fixture.QueuesAsync());
    }

    static string Lane(int id, string busName) => $"v3.development.bitween.xchangeservice.{id}{busName}".ToLowerInvariant();

    [Fact]
    public async Task A_work_group_processes_through_its_own_lane_which_moves_on_rename_and_goes_on_delete()
    {
        var (admin, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("main", key));

        var busName = $"Priority{Guid.NewGuid():N}"[..16];
        var groupId = (int)(await Api.Json(await admin.PostAsJsonAsync("/api/workgroups", new
        {
            name = Api.Unique("Priority"), busMessageName = busName,
            options = new { rabbitMqOptions = new { prefetch = 5 } }
        })))["id"]!;
        var lane = Lane(groupId, busName);
        Assert.True(await EventuallyAsync(q => q.Contains(lane) && q.Contains($"{lane}-result")), $"no lanes {lane}");

        async Task MoveSubscriptionAsync(int? workGroupId)
        {
            var subscription = (await Api.Json(await admin.GetAsync($"/api/subscriptions/{subscriptionId}"))).AsObject();
            subscription["workGroupId"] = workGroupId;
            await Api.Json(await admin.PostAsJsonAsync($"/api/subscriptions/{subscriptionId}", subscription));
        }

        await MoveSubscriptionAsync(groupId);
        Assert.Equal(HttpStatusCode.OK, (await Api.CallSyncAsync(fixture, urlName, key)).StatusCode);

        var refused = await admin.PostAsJsonAsync($"/api/workgroups/{groupId}/delete", new { });
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("CANT_BE_DELETED", await refused.Content.ReadAsStringAsync());

        // A new bus name is a new lane; the old one goes once nothing reads it.
        var renamed = $"Urgent{Guid.NewGuid():N}"[..14];
        await Api.Json(await admin.PostAsJsonAsync($"/api/workgroups/{groupId}", new
        {
            name = Api.Unique("Urgent"), busMessageName = renamed
        }));
        var newLane = Lane(groupId, renamed);
        Assert.True(await EventuallyAsync(q => q.Contains(newLane) && !q.Any(n => n.StartsWith(lane))),
            $"after the rename: {string.Join(", ", (await fixture.QueuesAsync()).Where(n => n.Contains($".{groupId}")))}");
        Assert.Equal(HttpStatusCode.OK, (await Api.CallSyncAsync(fixture, urlName, key)).StatusCode);

        await MoveSubscriptionAsync(null);
        await Api.Json(await admin.PostAsJsonAsync($"/api/workgroups/{groupId}/delete", new { }));
        Assert.True(await EventuallyAsync(q => !q.Any(n => n.StartsWith(newLane))), "the deleted group's lanes stayed");
        Assert.DoesNotContain((await Api.Json(await admin.GetAsync("/api/workgroups"))).ToJsonString(), renamed);

        // Back on the ungrouped lane, the subscription still works.
        Assert.Equal(HttpStatusCode.OK, (await Api.CallSyncAsync(fixture, urlName, key)).StatusCode);
        admin.Dispose();
    }
}
