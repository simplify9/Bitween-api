using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Xchanges;
using SW.Bitween.Services.XchangeStates;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Once the fill is done, the Exchanges page and dashboard read each exchange's own status and
/// retried flag. Every answer must be the one the result and retry tables gave before.
/// </summary>
[Collection("Bitween")]
public class XchangeStateReadTests(BitweenFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => ForgetFillAsync();

    /// <summary>Leaves the other tests on the result and retry tables, which they seed directly.</summary>
    public Task DisposeAsync() => ForgetFillAsync();

    [Fact]
    public async Task Reading_the_columns_answers_exactly_as_the_result_and_retry_tables_do()
    {
        var (documentId, expected) = await ArrangeAsync();

        var before = await AnswersAsync(documentId, ready: false);
        var after = await AnswersAsync(documentId, ready: true);

        Assert.Equal(before, after);

        // And they are the right answers, not merely the same ones.
        foreach (var (filter, ids) in expected)
            Assert.Equal(ids.OrderBy(i => i), before[$"search {filter}"].Split(',').Where(s => s.Length > 0 && !s.StartsWith("total")));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// One exchange of each status, a failure retried by another failure, and the fill run over all
    /// of it. Returns what each tab should list.
    /// </summary>
    private async Task<(int DocumentId, Dictionary<string, string[]> Expected)> ArrangeAsync()
    {
        string success, bad, failed, retried, retry, processing;
        int documentId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

            var doc = new Document(null, $"State Read Doc {Guid.NewGuid():N}"[..30], DocumentFormat.Json);
            db.Set<Document>().Add(doc);
            await db.SaveChangesAsync();
            documentId = doc.Id;
            var sub = new Subscription($"State Read Sub {doc.Id}", doc.Id) { Inactive = false };
            db.Set<Subscription>().Add(sub);
            await db.SaveChangesAsync();

            async Task<string> Exchange(Func<string, XchangeResult>? resultFor)
            {
                var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
                await db.SaveChangesAsync();
                if (resultFor != null)
                {
                    db.Add(resultFor(xchange.Id));
                    await db.SaveChangesAsync();
                }

                return xchange.Id;
            }

            success = await Exchange(id => new XchangeResult(id, null, null));
            bad = await Exchange(id => new XchangeResult(id, null, null, new XchangeFile("no", "r.json", true)));
            failed = await Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
            retried = await Exchange(id => new XchangeResult(id, null, null, exception: "boom"));
            processing = await Exchange(null);

            await xs.CreateXchange(sub, await db.Set<Xchange>().SingleAsync(x => x.Id == retried), new XchangeFile("{}"));
            await db.SaveChangesAsync();
            retry = await db.Set<Xchange>().Where(x => x.RetryFor == retried).Select(x => x.Id).SingleAsync();
            db.Add(new XchangeResult(retry, null, null, exception: "boom again"));
            await db.SaveChangesAsync();
        }

        // Labels every exchange from its result and retries, as on an upgraded database.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await db.Set<XchangeStateFill>().ExecuteDeleteAsync();
        }

        for (var run = 0; run < 20; run++)
        {
            await using var scope = fixture.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<XchangeStateFiller>().RunAsync(TimeSpan.FromSeconds(30))
                == XchangeStateFillPhase.Ready)
                break;
        }

        return (documentId, new Dictionary<string, string[]>
        {
            ["StatusFilter:1:0"] = [processing],
            ["StatusFilter:1:1"] = [success],
            ["StatusFilter:1:2"] = [bad],
            ["StatusFilter:1:3"] = [failed, retried, retry],
            ["StatusFilter:1:3&LatestOnly:1:true"] = [failed, retry],
            ["LatestOnly:1:true"] = [success, bad, failed, retry, processing],
        });
    }

    /// <summary>
    /// Everything that reads an exchange's state: the list and its total under each status with and
    /// without latest-only, the summary's counts, and the two dashboard reads.
    /// </summary>
    private async Task<Dictionary<string, string>> AnswersAsync(int documentId, bool ready)
    {
        await SetReadyAsync(ready);
        var answers = new Dictionary<string, string>();

        string[] filters =
        [
            "", "StatusFilter:1:0", "StatusFilter:1:1", "StatusFilter:1:2", "StatusFilter:1:3",
            "LatestOnly:1:true", "StatusFilter:1:3&LatestOnly:1:true", "StatusFilter:1:1&LatestOnly:1:true",
        ];
        foreach (var filter in filters)
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
            var request = Filters(documentId, filter);

            var found = (SearchyResponse<XchangeRow>)await new Search(db, xs, scope.Superuser())
                .Handle(new SearchyRequest(request) { PageSize = 50, CountRows = true });
            answers[$"search {filter}"] = string.Join(",",
                found.Result.Select(r => r.Id).OrderBy(i => i).Append($"total{found.TotalCount}"));

            var summary = (XchangeSummary)await new Summary(db, scope.Superuser(),
                    scope.ServiceProvider.GetRequiredService<SlowCounts>())
                .Handle(new SearchyRequest(request));
            answers[$"summary {filter}"] = JsonConvert.SerializeObject(summary.Counts);
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            answers["failures to act on"] = JsonConvert.SerializeObject(
                await new Resources.Dashboard.FailuresToActOn(db, scope.Superuser()).Handle());
            answers["retry summary"] = JsonConvert.SerializeObject(
                await new Resources.Dashboard.RetrySummary(db, scope.Superuser()).Handle());
        }

        return answers;
    }

    private static string[] Filters(int documentId, string filter) =>
        new[] { $"DocumentId:1:{documentId}" }
            .Concat(filter.Split('&', StringSplitOptions.RemoveEmptyEntries)).ToArray();

    private async Task SetReadyAsync(bool ready)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await db.Set<XchangeStateFill>().ExecuteUpdateAsync(s => s.SetProperty(f => f.Phase,
            ready ? XchangeStateFillPhase.Ready : XchangeStateFillPhase.Recheck));
        XchangeStateReadiness.Forget();
    }

    private async Task ForgetFillAsync()
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeStateFill>().ExecuteDeleteAsync();
        XchangeStateReadiness.Forget();
    }
}
