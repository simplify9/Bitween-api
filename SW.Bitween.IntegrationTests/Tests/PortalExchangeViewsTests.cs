using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The portal's Exchanges page reads our team's own search, counts and retry chain, limited to the
/// caller's partner and without what is ours to know.
/// </summary>
[Collection("Bitween")]
public class PortalExchangeViewsTests(BitweenFixture fixture)
{
    private record World(int Mine, int SeesErrors, int SeesNothingMore, string MyFailed, string TheirFailed, int SubscriptionId);

    private async Task<World> SetUpAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var doc = new Document(null, $"Views {Guid.NewGuid():N}", DocumentFormat.Json);
        var mine = new Partner($"Views mine {Guid.NewGuid():N}");
        var theirs = new Partner($"Views theirs {Guid.NewGuid():N}");
        db.AddRange(doc, mine, theirs);
        await db.SaveChangesAsync();
        var sub = new Subscription($"Views {Guid.NewGuid():N}", doc.Id) { Inactive = false };
        db.Add(sub);
        await db.SaveChangesAsync();

        async Task<string> Failed(Partner partner)
        {
            var x = await xs.CreateXchange(sub, new XchangeFile("{}"), gatewayPartner: partner);
            await db.SaveChangesAsync();
            db.Add(new XchangeResult(x.Id, null, null, exception: "Connection refused by 10.0.0.7"));
            await db.SaveChangesAsync();
            return x.Id;
        }

        async Task<int> Person(params string[] permissions)
        {
            var role = new Role($"Views role {Guid.NewGuid():N}", null, permissions, forPartners: true);
            db.Add(role);
            var account = new Account("Views", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, mine.Id);
            db.Add(account);
            await db.SaveChangesAsync();
            db.Add(new AccountRoleLink(account.Id, role.Id));
            await db.SaveChangesAsync();
            return account.Id;
        }

        return new World(mine.Id,
            await Person(Permissions.Portal.ExchangesView, Permissions.Portal.ExchangeErrorsView),
            await Person(Permissions.Portal.ExchangesView),
            await Failed(mine), await Failed(theirs), sub.Id);
    }

    private async Task<T> As<THandler, T>(int person, Func<THandler, Task<object>> act) where THandler : class
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return (T)await act(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private static SearchyRequest Filter(params string[] filters) =>
        new(string.Join("&", filters.Select(f => $"filter={f}"))) { PageSize = 50 };

    [Fact]
    public async Task Lists_only_theirs_with_what_their_role_shows()
    {
        var w = await SetUpAsync();
        var bySubscription = $"SubscriptionId:1:{w.SubscriptionId}";

        var withErrors = await As<Resources.Portal.SearchExchanges, SearchyResponse<XchangeRow>>(w.SeesErrors,
            h => h.Handle(Filter(bySubscription)));
        var row = Assert.Single(withErrors.Result);
        Assert.Equal(w.MyFailed, row.Id);
        Assert.Contains("Connection refused", row.Exception);
        Assert.Null(row.HandlerId);
        Assert.Null(row.InputKey);

        var plain = await As<Resources.Portal.SearchExchanges, SearchyResponse<XchangeRow>>(w.SeesNothingMore,
            h => h.Handle(Filter(bySubscription)));
        var plainRow = Assert.Single(plain.Result);
        Assert.Null(plainRow.Exception);
        Assert.Null(plainRow.InputUrl);
    }

    [Fact]
    public async Task Refuses_filters_on_what_they_may_not_read()
    {
        var w = await SetUpAsync();
        await Assert.ThrowsAsync<SWValidationException>(() => As<Resources.Portal.SearchExchanges, object>(w.SeesNothingMore,
            h => h.Handle(Filter("ErrorContains:1:refused"))));
        await Assert.ThrowsAsync<SWValidationException>(() => As<Resources.Portal.SearchExchanges, object>(w.SeesErrors,
            h => h.Handle(Filter("Exception:1:refused"))));
        await Assert.ThrowsAsync<SWValidationException>(() => As<Resources.Portal.SearchExchanges, object>(w.SeesErrors,
            h => h.Handle(Filter("HandlerId:1:x"))));

        // With the errors permission, the error filter works on their own.
        var found = await As<Resources.Portal.SearchExchanges, SearchyResponse<XchangeRow>>(w.SeesErrors,
            h => h.Handle(Filter($"SubscriptionId:1:{w.SubscriptionId}", "ErrorContains:1:refused")));
        Assert.Equal([w.MyFailed], found.Result.Select(r => r.Id).ToList());
    }

    [Fact]
    public async Task An_id_does_not_find_theirs_rolled_up_into_it()
    {
        var w = await SetUpAsync();
        // Theirs rolled up into another exchange: our wiring, which they aren't shown.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(new XchangeAggregation(w.MyFailed, w.TheirFailed));
            await db.SaveChangesAsync();
        }

        var byRollUp = Filter($"Id:1:{w.TheirFailed}");
        var found = await As<Resources.Portal.SearchExchanges, SearchyResponse<XchangeRow>>(w.SeesNothingMore,
            h => h.Handle(byRollUp));
        Assert.Empty(found.Result);
        var summary = await As<Resources.Portal.CountExchanges, XchangeSummary>(w.SeesNothingMore,
            h => h.Handle(Filter($"Id:1:{w.TheirFailed}")));
        Assert.Equal(0, summary.Counts.Failed);

        // Their own id still finds it.
        var own = await As<Resources.Portal.SearchExchanges, SearchyResponse<XchangeRow>>(w.SeesNothingMore,
            h => h.Handle(Filter($"Id:1:{w.MyFailed}")));
        Assert.Equal([w.MyFailed], own.Result.Select(r => r.Id).ToList());
    }

    [Fact]
    public async Task Counts_only_theirs()
    {
        var w = await SetUpAsync();
        var summary = await As<Resources.Portal.CountExchanges, XchangeSummary>(w.SeesNothingMore,
            h => h.Handle(Filter($"SubscriptionId:1:{w.SubscriptionId}")));
        Assert.Equal(1, summary.Counts.Failed);
    }

    [Fact]
    public async Task Shows_the_chain_of_theirs_only()
    {
        var w = await SetUpAsync();
        var tree = await As<Resources.Portal.ExchangeAttempts, XchangeRetryTree>(w.SeesNothingMore,
            h => h.Handle(new XchangeRetryTreeRequest { Id = w.MyFailed }));
        Assert.Equal(w.MyFailed, Assert.Single(tree.Nodes).Id);
        Assert.Null(tree.Nodes[0].Exception);

        await Assert.ThrowsAsync<SWNotFoundException>(() => As<Resources.Portal.ExchangeAttempts, object>(w.SeesErrors,
            h => h.Handle(new XchangeRetryTreeRequest { Id = w.TheirFailed })));
    }
}
