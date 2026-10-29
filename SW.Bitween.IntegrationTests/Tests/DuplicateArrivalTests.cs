using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The information type's duplicate window: the same payload from the same partner again within it
/// is reported, and dropped only when the type says so.
/// </summary>
[Collection("Bitween")]
public class DuplicateArrivalTests(BitweenFixture fixture)
{
    private async Task<(int SubscriptionId, int DocumentId, int PartnerId, int OtherPartnerId)> Setup(
        int window, bool drop)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, "Duplicates " + Guid.NewGuid().ToString("N")[..8], DocumentFormat.Json)
        {
            DuplicateInterval = window,
            DropDuplicates = drop,
        };
        var partner = new Partner("Duplicates partner " + Guid.NewGuid().ToString("N")[..8]);
        var other = new Partner("Duplicates other " + Guid.NewGuid().ToString("N")[..8]);
        db.AddRange(document, partner, other);
        await db.SaveChangesAsync();

        var subscription = new Subscription("Duplicates " + document.Id, document.Id, SubscriptionType.ApiCall, partner.Id)
            { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        // The document cache is shared across the collection and warm; a type made after it warmed
        // is invisible to it until it is told, as production tells it over the bus.
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return (subscription.Id, document.Id, partner.Id, other.Id);
    }

    private async Task<string> Submit(int subscriptionId, string payload, int? asPartner = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = asPartner == null ? null : await db.FindAsync<Partner>(asPartner.Value);
        return await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .SubmitSubscriptionXchange(subscriptionId, new XchangeFile(payload), gatewayPartner: partner);
    }

    private async Task<T> Read<T>(Func<BitweenDbContext, Task<T>> query)
    {
        await using var scope = fixture.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
    }

    [Fact]
    public async Task Without_a_window_the_same_payload_twice_is_two_exchanges()
    {
        var (sub, doc, _, _) = await Setup(window: 0, drop: false);

        var first = await Submit(sub, """{"order":1}""");
        var second = await Submit(sub, """{"order":1}""");

        Assert.NotEqual(first, second);
        Assert.Equal(0, await Read(db => db.Set<DuplicateArrival>().CountAsync(d => d.DocumentId == doc)));
    }

    [Fact]
    public async Task A_duplicate_is_let_through_tagged_and_counted_until_the_type_drops_them()
    {
        var (sub, doc, _, _) = await Setup(window: 60, drop: false);

        var first = await Submit(sub, """{"order":2}""");
        var second = await Submit(sub, """{"order":2}""");
        var different = await Submit(sub, """{"order":3}""");

        Assert.NotEqual(first, second);
        var tagged = await Read(db => db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == second));
        Assert.Contains($"duplicate-of: {first}", tagged.References);
        var untouched = await Read(db => db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == different));
        Assert.DoesNotContain(untouched.References ?? [], r => r.StartsWith("duplicate-of"));

        var reported = await Read(db => db.Set<DuplicateArrival>().AsNoTracking().SingleAsync(d => d.DocumentId == doc));
        Assert.Equal(first, reported.OriginalXchangeId);
        Assert.False(reported.Dropped);

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var row = (DocumentRow)await ActivatorUtilities.CreateInstance<Resources.Documents.Get>(scope.ServiceProvider).Handle(doc);
        Assert.Equal(1, row.DuplicatesLastWeek);
        Assert.Equal(0, row.DuplicatesDroppedLastWeek);
    }

    [Fact]
    public async Task A_type_that_drops_duplicates_answers_with_the_exchange_repeated()
    {
        var (sub, doc, _, _) = await Setup(window: 60, drop: true);

        var first = await Submit(sub, """{"order":4}""");
        var second = await Submit(sub, """{"order":4}""");

        Assert.Equal(first, second);
        Assert.Equal(1, await Read(db => db.Set<Xchange>().CountAsync(x => x.DocumentId == doc)));
        Assert.True(await Read(db => db.Set<DuplicateArrival>().AnyAsync(d => d.DocumentId == doc && d.Dropped)));
    }

    [Fact]
    public async Task The_same_payload_from_another_partner_is_not_a_duplicate()
    {
        var (sub, doc, partner, other) = await Setup(window: 60, drop: true);

        var first = await Submit(sub, """{"order":5}""", partner);
        var second = await Submit(sub, """{"order":5}""", other);

        Assert.NotEqual(first, second);
        Assert.Equal(0, await Read(db => db.Set<DuplicateArrival>().CountAsync(d => d.DocumentId == doc)));
    }
}
