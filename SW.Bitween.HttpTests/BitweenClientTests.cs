using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Client;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// SimplyWorks.Bitween.Client against a real Bitween: what clients build their own screens on. A change
/// to the exchange endpoints that breaks the client breaks these, so it is found here and not by a
/// client after they upgrade.
/// </summary>
[Collection("Http")]
public class BitweenClientTests(HttpFixture fixture)
{
    /// <summary>A gateway whose partner has made <paramref name="calls"/> exchanges, each sent its own body.</summary>
    async Task<(int PartnerId, string[] Bodies)> ExchangesAsync(int calls)
    {
        var (admin, urlName, _, partnerId, _) = await Api.GatewayAsync(fixture);
        var key = await Api.NewKeyAsync(admin);
        await Api.SetKeysAsync(admin, partnerId, ("primary", key));
        var bodies = Enumerable.Range(1, calls).Select(i => $"{{\"order\":\"{Guid.NewGuid():N}\",\"line\":{i}}}").ToArray();
        foreach (var body in bodies)
            using (var answer = await Api.CallSyncAsync(fixture, urlName, key, body))
                Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return (partnerId, bodies);
    }

    /// <summary>A client signed in as a new service account whose one role has <paramref name="permissions"/>.</summary>
    async Task<BitweenClient> ClientAsync(params string[] permissions)
    {
        using var admin = await fixture.AdminAsync();
        var roleId = await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("client role"), permissions });
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("client"), roleIds = new[] { roleId } });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken",
            new { name = "client", expiresInDays = 30 }));
        return new BitweenClient(fixture.Client(), (string)issued["token"]!);
    }

    [Fact]
    public async Task Searches_reads_and_retries_exchanges_as_the_Exchanges_page_does()
    {
        var (partnerId, bodies) = await ExchangesAsync(2);
        using var bitween = await ClientAsync("exchanges.view", "exchanges.operate");
        var partners = new ExchangeQuery { PartnerIds = [partnerId] };

        var page = await bitween.Exchanges.SearchAsync(partners);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Result.Count);
        Assert.All(page.Result, x => Assert.Equal(partnerId, x.PartnerId));
        Assert.Single((await bitween.Exchanges.SearchAsync(partners, page: 1, size: 1)).Result);
        Assert.Equal(0, (await bitween.Exchanges.SearchAsync(partners, count: false)).TotalCount);

        // Status and time are filters the server is particular about: each has to come through as it reads them.
        Assert.Equal(2, (await bitween.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId], Status = ExchangeStatus.Success })).TotalCount);
        Assert.Equal(0, (await bitween.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId], Status = ExchangeStatus.Failed })).TotalCount);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(2, (await bitween.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId], From = now.AddHours(-1), To = now.AddHours(1) })).TotalCount);
        Assert.Equal(0, (await bitween.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId], From = now.AddHours(1) })).TotalCount);

        var inputs = await Task.WhenAll(page.Result.Select(x => bitween.Exchanges.ReadFileAsync(x.InputKey)));
        Assert.Equal(bodies.Order(), inputs.Order());
        var first = page.Result[Array.IndexOf(inputs, bodies[0])];
        Assert.Equal(first.Id, (await bitween.Exchanges.GetAsync(first.Id))!.Id);
        Assert.Null(await bitween.Exchanges.GetAsync(Guid.NewGuid().ToString("N")));

        var retryId = await bitween.Exchanges.RetryAsync(first.Id);
        Assert.NotNull(retryId);
        Assert.NotEqual(first.Id, retryId);
        // The id search finds the retry too; asked for one exchange, it is that one.
        Assert.Equal(first.Id, (await bitween.Exchanges.GetAsync(first.Id))!.Id);
        Assert.Equal(first.Id, (await bitween.Exchanges.GetAsync(retryId!))!.RetryFor);

        var tree = await bitween.Exchanges.GetRetryTreeAsync(retryId!);
        Assert.Equal(first.Id, tree.RootId);
        Assert.Equal([first.Id, retryId], tree.Nodes.Select(n => n.Id).ToArray());

        var again = await Assert.ThrowsAsync<BitweenClientException>(() => bitween.Exchanges.RetryAsync(first.Id));
        Assert.Equal(HttpStatusCode.BadRequest, again.Status);
        Assert.Equal("ALREADY_RETRIED", again.Code);
    }

    [Fact]
    public async Task Bulk_retries_and_exports_a_selection()
    {
        var (partnerId, _) = await ExchangesAsync(3);
        using var bitween = await ClientAsync("exchanges.view", "exchanges.operate");
        var partners = new ExchangeQuery { PartnerIds = [partnerId] };
        var ids = (await bitween.Exchanges.SearchAsync(partners)).Result.Select(x => x.Id).ToList();
        // A bulk retry leaves out what succeeded, so these failed.
        await fixture.InDbAsync(db => db.Set<Domain.XchangeResult>().Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Success, false).SetProperty(r => r.Exception, "Refused by the client test")));
        var failed = new ExchangeQuery { PartnerIds = [partnerId], Status = ExchangeStatus.Failed, ErrorContains = "client TEST" };
        Assert.Equal(3, (await bitween.Exchanges.SearchAsync(failed)).TotalCount);

        var preview = await bitween.Exchanges.PreviewBulkRetryAsync(ExchangeSelection.Matching(failed, except: [ids[0]]));
        Assert.Equal(2, preview.Selected);
        Assert.Equal(2, preview.WillRetry);
        Assert.Equal(3, (await bitween.Exchanges.SearchAsync(partners)).TotalCount);

        var plan = await bitween.Exchanges.BulkRetryAsync(ExchangeSelection.Of(ids.Take(2)));
        Assert.Equal(2, plan.WillRetry);
        Assert.Equal(5, (await bitween.Exchanges.SearchAsync(partners)).TotalCount);
        Assert.Equal(3, (await bitween.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId], LatestOnly = true })).TotalCount);

        var check = await bitween.Exchanges.CheckExportAsync(ExchangeSelection.Of(ids));
        Assert.Equal(3, check.Count);
        Assert.Equal(0, check.WithoutFiles);

        await using var zip = await bitween.Exchanges.ExportAsync(ExchangeSelection.Of(ids));
        using var copy = new MemoryStream();
        await zip.CopyToAsync(copy);
        using var archive = new ZipArchive(copy, ZipArchiveMode.Read);
        Assert.All(ids, id => Assert.Contains(archive.Entries, e => e.FullName.Contains(id)));
    }

    [Fact]
    public async Task Says_what_Bitween_refused_and_why()
    {
        var (partnerId, _) = await ExchangesAsync(1);
        using var viewer = await ClientAsync("exchanges.view");
        var id = (await viewer.Exchanges.SearchAsync(new ExchangeQuery { PartnerIds = [partnerId] })).Result.Single().Id;

        var forbidden = await Assert.ThrowsAsync<BitweenClientException>(() => viewer.Exchanges.RetryAsync(id));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<BitweenClientException>(() => viewer.ScheduledRetries.RunNowAsync(id))).Status);
        Assert.NotNull((await viewer.ScheduledRetries.SearchAsync(new ScheduledRetryQuery { From = DateTimeOffset.UtcNow })).Result);

        using var stranger = new BitweenClient(fixture.Client(), "bwt_" + new string('x', 40));
        var refused = await Assert.ThrowsAsync<BitweenClientException>(() => stranger.Exchanges.SearchAsync());
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.StartsWith("This token isn't valid", refused.Message);
    }
}
