using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>GET /api/ops/background: the system jobs, scheduled at startup, and the outbox.</summary>
[Collection("Http")]
public class BackgroundTests(HttpFixture fixture)
{
    [Fact]
    public async Task The_system_jobs_are_scheduled_and_say_when_they_run_next()
    {
        using var admin = await fixture.AdminAsync();
        System.Text.Json.Nodes.JsonNode body = null;
        // Startup schedules them in the background; give it a moment on a busy machine.
        for (var i = 0; i < 40; i++)
        {
            body = await Api.Json(await admin.GetAsync("/api/ops/background"));
            if (body["jobs"]!.AsArray().All(j => (bool)j!["scheduled"]!)) break;
            await Task.Delay(250);
        }

        var jobs = body!["jobs"]!.AsArray();
        Assert.Equal(["Automatic retries", "Exchange retention", "Receive attempt clean-up", "Broker deduplication clean-up"],
            jobs.Select(j => (string)j!["name"]).ToArray());
        Assert.All(jobs, j =>
        {
            Assert.True((bool)j!["scheduled"]!, (string)j["name"]);
            Assert.NotNull(j["nextRunOn"]);
            Assert.False(string.IsNullOrEmpty((string)j["cron"]));
        });

        var outbox = body["outbox"]!;
        Assert.True((int)outbox["pending"]! >= 0);
        Assert.True((int)outbox["publishedLastHour"]! >= 0);
    }
}
