using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// The exchange summary by the route the list calls it on, <c>GET /api/xchanges/summary</c>: the
/// named searchy handler is reachable, takes the list's own filter parameters from the query
/// string, and answers in the camel-cased shape the client reads.
/// </summary>
[Collection("Http")]
public class ExchangeSummaryHttpTests(HttpFixture fixture)
{
    [Fact]
    public async Task The_summary_answers_on_its_route_with_counts_and_zero_filled_buckets()
    {
        using var admin = await fixture.AdminAsync();
        // An information type of its own, so nothing else the suite created is counted.
        var documentId = await Api.CreateAsync(admin, "/api/documents",
            new { name = Api.Unique("Summary"), documentFormat = "Json" });

        var to = new DateTime(2025, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var from = to.AddHours(-1);
        string Param(string filter) => "filter=" + Uri.EscapeDataString(filter);
        var query = string.Join("&",
            Param($"DocumentId:1:{documentId}"),
            // Ignored, not refused: the summary is the breakdown the status tabs show.
            Param("StatusFilter:1:3"),
            Param("ErrorContains:4:connection refused"),
            Param("IsRetry:1:false"),
            Param($"StartedOn:6:{from:yyyy-MM-ddTHH:mm:ss.fffZ}"),
            Param($"StartedOn:8:{to:yyyy-MM-ddTHH:mm:ss.fffZ}"));

        var summary = await Api.Json(await admin.GetAsync($"/api/xchanges/summary?{query}"));

        var counts = summary["counts"]!;
        foreach (var status in new[] { "processing", "success", "badResponse", "failed" })
            Assert.Equal(0, (int)counts[status]!);

        // An hour is 61 one-minute buckets counting both ends, one too many, so five minutes.
        Assert.Equal(5, (int)summary["bucketMinutes"]!);
        var buckets = summary["buckets"]!.AsArray();
        Assert.Equal(13, buckets.Count);
        Assert.Equal(from, DateTime.Parse((string)buckets[0]!["start"]!, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));
        Assert.All(buckets, b => Assert.Equal(0, (int)b!["failed"]!));
    }
}
