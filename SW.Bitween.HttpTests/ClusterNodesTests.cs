using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>GET /api/cluster/nodes: the nodes, as their heartbeats describe them.</summary>
[Collection("Http")]
public class ClusterNodesTests(HttpFixture fixture)
{
    [Fact]
    public async Task The_node_that_answers_is_listed_online_with_its_runtimes()
    {
        using var admin = await fixture.AdminAsync();
        System.Text.Json.Nodes.JsonNode body = null;
        // The heartbeat writes when the node starts; give it a moment on a busy machine.
        for (var i = 0; i < 20; i++)
        {
            body = await Api.Json(await admin.GetAsync("/api/cluster/nodes"));
            if (body["nodes"]!.AsArray().Count > 0) break;
            await Task.Delay(250);
        }

        var answeredBy = (string)body!["answeredBy"];
        Assert.Equal($"{Environment.MachineName}:{Environment.ProcessId}", answeredBy);
        var self = body["nodes"]!.AsArray().Single(n => (string)n!["name"] == answeredBy)!;
        Assert.True((bool)self["online"]!);
        Assert.Equal(Environment.MachineName, (string)self["host"]);
        Assert.Contains("python", self["runtimes"]!.AsArray().Select(r => (string)r));
        Assert.False((bool)self["dataSources"]!);
    }

    [Fact]
    public async Task Signed_out_it_is_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client().GetAsync("/api/cluster/nodes")).StatusCode);
    }
}
