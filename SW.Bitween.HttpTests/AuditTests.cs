using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The audit trail over HTTP: changes made through the API are recorded against the record and the
/// person who made them, with what changed, and the timeline for one record shows only that record.
/// </summary>
[Collection("Http")]
public class AuditTests(HttpFixture fixture)
{
    [Fact]
    public async Task A_partner_s_timeline_shows_who_created_and_renamed_it_and_what_changed()
    {
        using var admin = await fixture.AdminAsync();
        var original = Api.Unique("Audited");
        var partnerId = await Api.CreateAsync(admin, "/api/partners", new { name = original });
        var renamed = Api.Unique("Renamed");
        await Api.Json(await admin.PostAsJsonAsync($"/api/partners/{partnerId}", new { name = renamed }));
        // Another partner's history must not leak into this one's timeline.
        await Api.CreateAsync(admin, "/api/partners", new { name = Api.Unique("Bystander") });

        var timeline = await Api.Json(await admin.GetAsync($"/api/audit?entityName=Partner&entityKey={partnerId}"));
        var rows = timeline["result"]!.AsArray();

        Assert.Equal(rows.Count, (int)timeline["totalCount"]!);
        Assert.All(rows, r => Assert.Equal(partnerId.ToString(), (string)r!["entityKey"]!));
        Assert.Equal(["Modified", "Added"], rows.Select(r => (string)r!["state"]!).ToArray());

        var rename = rows[0]!["changes"]!["Name"]!;
        Assert.Equal(original, (string)rename["old"]!);
        Assert.Equal(renamed, (string)rename["new"]!);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty((string?)r!["userDisplayName"]), "who made the change"));
    }
}
