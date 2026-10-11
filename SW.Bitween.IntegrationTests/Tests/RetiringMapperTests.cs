using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The old mapper is offered only while a subscription still uses it.
/// </summary>
/// <remarks>
/// Both directions are asserted in one test on purpose. The answer is a fact about the whole
/// database, so a test that only asserted one direction would be passing or failing on whatever
/// the other tests in this collection happened to leave behind.
/// </remarks>
[Collection("Bitween")]
public class RetiringMapperTests
{
    private const string OldMapper = "NativeJSONMapper";
    private const string NewMapper = "NativeMapper";

    private readonly BitweenFixture _fixture;

    public RetiringMapperTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>The mapper ids the adapter catalog offers, as the picker would list them.</summary>
    private async Task<List<string>> ListedMappers()
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities
            .CreateInstance<Resources.Adapters.SearchVersioned>(scope.ServiceProvider);

        var result = await handler.Handle(new AdapterSearchRequest { Prefix = "mappers" });

        // The handler returns anonymous types; going through JSON reads them the way the browser
        // does rather than through reflection.
        return JArray.Parse(JsonConvert.SerializeObject(result))
            .Select(row => row["Key"]!.ToString())
            .ToList();
    }

    /// <summary>A subscription saved with <paramref name="mapperId"/>.</summary>
    private async Task<int> SubscriptionUsing(string mapperId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("Retiring doc"), DocumentFormat.Json);
        db.Set<Document>().Add(document);
        var partner = new Partner(Unique("Retiring partner"));
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var subscription = new Subscription(
            Unique("Retiring sub"), document.Id, SubscriptionType.Internal, partner.Id)
        {
            MapperId = mapperId,
        };
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        return subscription.Id;
    }

    private async Task SetMapper(int subscriptionId, string? mapperId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        subscription.MapperId = mapperId;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Every subscription this database already holds on the old mapper, moved off it.
    /// </summary>
    /// <remarks>
    /// Safe to do to a shared database: the only other tests that put a subscription on the old
    /// mapper build a fresh one inside each test, so none of them reads a row left by an earlier
    /// one. A collection's tests also run one at a time, so nothing is arranging while this runs.
    /// </remarks>
    private async Task<List<int>> ParkExistingUsers()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var existing = await db.Set<Subscription>()
            .Where(s => s.MapperId != null && s.MapperId.ToLower() == "nativejsonmapper")
            .ToListAsync();

        foreach (var subscription in existing) subscription.MapperId = null;
        await db.SaveChangesAsync();

        return existing.Select(s => s.Id).ToList();
    }

    [Fact]
    public async Task The_old_mapper_is_listed_only_while_a_subscription_still_uses_it()
    {
        await ParkExistingUsers();

        // Nothing on it: it is gone from the picker, and the new mapper is still there - a filter
        // that took out more than it should would pass an assertion about the old one alone.
        var withoutUsers = await ListedMappers();
        Assert.DoesNotContain(OldMapper, withoutUsers);
        Assert.Contains(NewMapper, withoutUsers);

        // One subscription on it anywhere in the database is enough to bring it back everywhere.
        var subscriptionId = await SubscriptionUsing(OldMapper);
        Assert.Contains(OldMapper, await ListedMappers());

        // And moving that last one across takes it away again, which is the whole point: the
        // migration finishing is what retires the mapper.
        await SetMapper(subscriptionId, NewMapper);
        Assert.DoesNotContain(OldMapper, await ListedMappers());
    }

    [Fact]
    public async Task An_inactive_subscription_still_counts_as_using_it()
    {
        await ParkExistingUsers();

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, Unique("Retiring inactive doc"), DocumentFormat.Json);
            db.Set<Document>().Add(document);
            var partner = new Partner(Unique("Retiring inactive partner"));
            db.Set<Partner>().Add(partner);
            await db.SaveChangesAsync();

            var subscription = new Subscription(
                Unique("Retiring inactive sub"), document.Id, SubscriptionType.Internal, partner.Id)
            {
                MapperId = OldMapper,
                Inactive = true,
            };
            db.Set<Subscription>().Add(subscription);
            await db.SaveChangesAsync();
        }

        // It holds a template and can be switched back on, so it keeps the mapper listed. Withheld
        // instead, this subscription's own picker would show nothing selected.
        Assert.Contains(OldMapper, await ListedMappers());
    }

    [Fact]
    public async Task Casing_that_drifted_still_counts()
    {
        await ParkExistingUsers();
        await SubscriptionUsing("nativejsonmapper");

        // The run-time lookup that resolves a mapper matches case-insensitively, so a row stored
        // this way is a subscription running on the old mapper and has to keep it listed.
        Assert.Contains(OldMapper, await ListedMappers());
    }
}
