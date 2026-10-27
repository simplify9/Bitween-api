using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Mirror;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The lists on a mirror: which of staging's items prod has too, by name, and how their tests last ran
/// - a gateway's being those of the subscriptions it leads to - with the filter the list pages page
/// through. And the mirror's work, which tests several subscriptions one after another.
/// </summary>
[Collection("Bitween")]
public class MirrorListTests(BitweenFixture fixture)
{
    [Fact]
    public async Task The_lists_keep_what_prod_has_too_and_what_its_test_says()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        string Name(string what) => $"Mirror list {run} {what}";
        int tested, untested, gatewayId, busId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = new Partner(Name("partner"));
            var document = new Document(null, Name("type"), DocumentFormat.Json);
            db.AddRange(partner, document);
            await db.SaveChangesAsync();
            var subscriptions = new[] { "tested", "untested", "staging only" }
                .Select(what => new Subscription(Name(what), document.Id, SubscriptionType.GatewayApiCall)).ToList();
            db.AddRange(subscriptions);
            await db.SaveChangesAsync();
            var gateway = new ApiGateway
            {
                Name = Name("gateway"), UrlName = $"mirror-list-{run}",
                Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscriptions[0].Id }],
            };
            var bus = new BusGateway
            {
                Name = Name("bus"), DocumentId = document.Id,
                Routes = subscriptions.Select(s => new BusGatewayRoute { SubscriptionId = s.Id }).ToList(),
            };
            db.AddRange(gateway, bus);
            await db.SaveChangesAsync();
            (tested, untested, gatewayId, busId) = (subscriptions[0].Id, subscriptions[1].Id, gateway.Id, bus.Id);

            // Prod's lists as staging last read them, and one test that ran.
            var url = scope.ServiceProvider.GetRequiredService<BitweenOptions>().Mirror.SourceUrl;
            MirrorService.ProdNames[(url, "subscriptions")] = (DateTimeOffset.UtcNow, new HashSet<string> { Name("tested"), Name("untested") });
            MirrorService.ProdNames[(url, "apigateways")] = (DateTimeOffset.UtcNow, new HashSet<string> { Name("gateway") });
            MirrorService.ProdNames[(url, "busgateways")] = (DateTimeOffset.UtcNow, new HashSet<string> { Name("bus") });
            await scope.ServiceProvider.GetRequiredService<MirrorService>()
                .SaveVerdictAsync(Name("tested"), new MirrorTestVerdict("same", DateTimeOffset.UtcNow, 3, 0, 0, 0));
        }

        Assert.Equal(new[] { tested, untested }, await SubscriptionsAsync(run, "fromProd"));
        Assert.Equal(new[] { tested }, await SubscriptionsAsync(run, "same"));
        Assert.Equal(new[] { untested }, await SubscriptionsAsync(run, "untested"));
        Assert.Empty(await SubscriptionsAsync(run, "different"));

        var gatewayItem = Assert.Single((await ListedAsync("apiGateways")).Items, i => i.Id == gatewayId);
        Assert.Equal(("same", 1, 1), (gatewayItem.Verdict, gatewayItem.Tested, gatewayItem.Of));
        // The subscription only staging has isn't one prod's traffic can test.
        var busItem = Assert.Single((await ListedAsync("busGateways")).Items, i => i.Id == busId);
        Assert.Equal(("same", 1, 2), (busItem.Verdict, busItem.Tested, busItem.Of));

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var search = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Search>(scope.ServiceProvider);
            var found = (SearchyResponse<ApiGatewayRow>)await search.Handle(new SearchyRequest($"filter=Mirror:1:same&filter=Name:4:{run}") { PageSize = 50 });
            Assert.Equal(gatewayId, Assert.Single(found.Result).Id);
        }
        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var search = ActivatorUtilities.CreateInstance<Resources.BusGateways.Search>(scope.ServiceProvider);
            var found = (SearchyResponse<BusGatewayRow>)await search.Handle(new SearchyRequest($"filter=Mirror:1:untested&filter=Name:4:{run}") { PageSize = 50 });
            Assert.Empty(found.Result);
        }

        // A subscription prod had nothing of to take is said to, and says nothing of how the rest ran.
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MirrorService>()
                .SaveVerdictAsync(Name("untested"), new MirrorTestVerdict(MirrorService.NoTraffic, DateTimeOffset.UtcNow, 0, 0, 0, 0));
        Assert.Equal(new[] { untested }, await SubscriptionsAsync(run, MirrorService.NoTraffic));
        busItem = Assert.Single((await ListedAsync("busGateways")).Items, i => i.Id == busId);
        Assert.Equal(("same", 2, 2), (busItem.Verdict, busItem.Tested, busItem.Of));
    }

    [Fact]
    public async Task Tests_of_several_run_one_after_another_and_one_that_fails_doesnt_stop_the_rest()
    {
        var (work, ran, hold) = await WorkAsync();
        Assert.Null(work.Start([
            Job(ran, MirrorWork.Sampling, "A", () => hold.Task),
            Job(ran, MirrorWork.Rehearsing, "A"),
            Job(ran, MirrorWork.Sampling, "B", () => throw new MirrorRefusal("Prod has nothing of B to take.")),
            Job(ran, MirrorWork.Rehearsing, "C"),
        ]));

        await Until(() => ran.Count == 1);
        Assert.Equal("A", work.Running.Subject);
        Assert.Equal(new[] { "B", "C" }, work.Waiting);
        // Nothing else starts while they run.
        Assert.Same(work.Running, work.Start(MirrorWork.Sampling, (_, _, _) => Task.CompletedTask, "D"));

        hold.SetResult();
        await Until(() => work.Running == null);
        Assert.Equal(new[] { "samples A", "rehearsal A", "samples B", "rehearsal C" }, ran);
        Assert.Equal("Prod has nothing of B to take.", work.LastError(MirrorWork.Sampling, "B"));
        Assert.Null(work.LastError(MirrorWork.Rehearsing, "C"));
    }

    [Fact]
    public async Task Stopping_drops_the_tests_waiting_and_the_one_under_way_finishes()
    {
        var (work, ran, hold) = await WorkAsync();
        Assert.Null(work.Start([Job(ran, MirrorWork.Sampling, "A", () => hold.Task), Job(ran, MirrorWork.Rehearsing, "B")]));
        await Until(() => ran.Count == 1);

        Assert.Equal(1, work.Stop());
        Assert.Empty(work.Waiting);

        hold.SetResult();
        await Until(() => work.Running == null);
        Assert.Equal(new[] { "samples A" }, ran);
    }

    async Task<(MirrorWork Work, ConcurrentQueue<string> Ran, TaskCompletionSource Hold)> WorkAsync()
    {
        await using var scope = fixture.CreateScope();
        return (new MirrorWork(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MirrorWork>.Instance), new(), new());
    }

    static MirrorJob Job(ConcurrentQueue<string> ran, string kind, string subject, Func<Task> body = null) =>
        new(kind, subject, async (_, _, _) =>
        {
            ran.Enqueue($"{kind} {subject}");
            if (body != null) await body();
        });

    [Fact]
    public async Task The_prod_filter_is_only_for_whoever_may_view_the_mirror()
    {
        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer($"no-mirror-{Guid.NewGuid():N}");
        var search = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Search>(scope.ServiceProvider);

        // A Viewer sees the subscriptions, but not which of them prod has, or how their tests went.
        await search.Handle(new SearchyRequest("filter=Name:4:anything") { PageSize = 5 });
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => search.Handle(new SearchyRequest("filter=Mirror:1:fromProd") { PageSize = 5 }, lookup: true));
    }

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    async Task<int[]> SubscriptionsAsync(string run, string mirror)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var search = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Search>(scope.ServiceProvider);
        var found = (SearchyResponse<SubscriptionSearch>)await search.Handle(
            new SearchyRequest($"filter=Mirror:1:{mirror}&filter=Name:4:{run}") { PageSize = 50 });
        return found.Result.Select(r => r.Id).Order().ToArray();
    }

    async Task<MirrorListed> ListedAsync(string section)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MirrorService>().ListedAsync(section);
    }
}
