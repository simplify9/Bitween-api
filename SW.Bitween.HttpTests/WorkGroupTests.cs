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
/// it - refused while a subscription is in it - takes the lanes away.
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

    [Fact]
    public async Task Parallel_exchanges_are_saved_with_the_group_and_attached_to_its_work_lane_but_not_its_results_lane()
    {
        var admin = await fixture.AdminAsync();
        var busName = $"Parallel{Guid.NewGuid():N}"[..16];

        var zero = await admin.PostAsJsonAsync("/api/workgroups", new
        {
            name = Api.Unique("Parallel"), busMessageName = busName,
            options = new { rabbitMqOptions = new { prefetch = 6, concurrency = 0 } }
        });
        Assert.False(zero.IsSuccessStatusCode);

        var groupId = (int)(await Api.Json(await admin.PostAsJsonAsync("/api/workgroups", new
        {
            name = Api.Unique("Parallel"), busMessageName = busName,
            options = new { rabbitMqOptions = new { prefetch = 6, concurrency = 3 } }
        })))["id"]!;

        var saved = (await Api.Json(await admin.GetAsync("/api/workgroups?offset=0&limit=1000")))["result"]!.AsArray()
            .Single(g => (int)g!["id"]! == groupId)!;
        Assert.Equal(3, (int)saved["options"]!["rabbitMqOptions"]!["concurrency"]!);

        // Queue health reads the broker, which shows the lanes once the refresh has attached them.
        var lane = Lane(groupId, busName);
        Assert.True(await EventuallyAsync(q => q.Contains(lane) && q.Contains($"{lane}-result")), $"no lanes {lane}");
        System.Text.Json.Nodes.JsonArray rows = null!;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            rows = (await Api.Json(await admin.GetAsync("/api/ops/consumers"))).AsArray();
            if (rows.Count(r => (int?)r!["workGroupId"] == groupId) == 2) break;
            await Task.Delay(500);
        }
        var work = rows.Single(r => (int?)r!["workGroupId"] == groupId && (string)r!["lane"]! == "Work")!;
        var results = rows.Single(r => (int?)r!["workGroupId"] == groupId && (string)r!["lane"]! == "Notifications")!;
        Assert.Equal(3, (int)work["concurrency"]!);
        Assert.Equal(1, (int)results["concurrency"]!);

        await Api.Json(await admin.PostAsJsonAsync($"/api/workgroups/{groupId}/delete", new { }));
        admin.Dispose();
    }

    [Fact]
    public async Task A_second_work_group_with_a_name_already_taken_is_refused_and_so_is_a_rename_onto_one()
    {
        // Two applies running at once each made one; their subscriptions ended up split between the two.
        var admin = await fixture.AdminAsync();
        var name = Api.Unique("Acme ERP");
        var first = (int)(await Api.Json(await admin.PostAsJsonAsync("/api/workgroups", new { name, busMessageName = "AcmeErp" })))["id"]!;

        var again = await admin.PostAsJsonAsync("/api/workgroups", new { name = $"  {name.ToUpperInvariant()} ", busMessageName = "AcmeErp2" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("already a work group named", await again.Content.ReadAsStringAsync());

        var other = (int)(await Api.Json(await admin.PostAsJsonAsync("/api/workgroups", new { name = Api.Unique("Other"), busMessageName = "Other" })))["id"]!;
        var rename = await admin.PostAsJsonAsync($"/api/workgroups/{other}", new { name, busMessageName = "Other" });
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);

        // Saving a group under its own name is no clash.
        Assert.True((await admin.PostAsJsonAsync($"/api/workgroups/{first}", new { name, busMessageName = "AcmeErp" })).IsSuccessStatusCode);
    }
}
