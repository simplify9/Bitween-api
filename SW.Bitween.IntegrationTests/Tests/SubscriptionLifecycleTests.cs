using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Creating, changing and removing an integration — the entity the whole product is arranged
/// around, and the one the redesigned UI touches on nearly every screen.
/// </summary>
/// <remarks>
/// The delete path gets most of the attention here. All four references are RESTRICT in the
/// database, so a delete was always refused; what the handler adds is <em>saying what is holding
/// it</em> instead of letting a foreign key violation surface as a 500. That message is the whole
/// feature, so these tests assert on its content rather than just on the refusal — a check that
/// only asserted "it threw" would still pass if the message went back to being useless.
/// </remarks>
[Collection("Bitween")]
public class SubscriptionLifecycleTests
{
    private readonly BitweenFixture _fixture;

    public SubscriptionLifecycleTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>An information type and partner to hang integrations off.</summary>
    private async Task<(int documentId, int partnerId)> Groundwork()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("Lifecycle doc"), DocumentFormat.Json);
        db.Set<Document>().Add(document);
        var partner = new Partner(Unique("Lifecycle partner"));
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        return (document.Id, partner.Id);
    }

    private async Task<int> CreateSubscription(string name, int documentId, int partnerId,
        SubscriptionType type = SubscriptionType.ApiCall)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);

        var created = await handler.Handle(new SubscriptionCreate
        {
            Name = name,
            DocumentId = documentId,
            PartnerId = partnerId,
            Type = type,
        });

        return (int)created;
    }

    private async Task Delete(int subscriptionId)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Delete>(scope.ServiceProvider);
        await handler.Handle(subscriptionId);
    }

    [Fact]
    public async Task An_integration_can_be_created_changed_and_removed()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Round trip"), documentId, partnerId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var stored = await db.Set<Subscription>().SingleAsync(s => s.Id == id);

            // Every constructor starts an integration inactive, so nothing begins running the
            // moment it is saved half-configured.
            Assert.True(stored.Inactive);
        }

        await Delete(id);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            Assert.False(await db.Set<Subscription>().AnyAsync(s => s.Id == id));
        }
    }

    [Fact]
    public async Task Deleting_names_the_bus_gateway_route_still_pointing_at_it()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Routed"), documentId, partnerId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = new BusGateway { Name = "Orders bus", DocumentId = documentId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();

            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = id });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Delete(id));

        // The operator has to be able to go straight to the thing holding it — a bare refusal
        // sends them hunting through every gateway by hand.
        Assert.Contains("Orders bus", ex.Message);
        Assert.Contains("route", ex.Message);
    }

    [Fact]
    public async Task Deleting_names_the_aggregation_still_pointing_at_it()
    {
        var (documentId, partnerId) = await Groundwork();
        var source = await CreateSubscription(Unique("Aggregated source"), documentId, partnerId);

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);
            await handler.Handle(new SubscriptionCreate
            {
                Name = "Nightly rollup",
                DocumentId = documentId,
                PartnerId = partnerId,
                AggregationForId = source,
                Type = SubscriptionType.Aggregation,
            });
        }

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Delete(source));

        Assert.Contains("Nightly rollup", ex.Message);
        Assert.Contains("aggregation", ex.Message);
    }

    [Fact]
    public async Task Deleting_lists_every_holder_at_once_rather_than_one_at_a_time()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Popular"), documentId, partnerId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            var gateway = new BusGateway { Name = "First bus", DocumentId = documentId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();
            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = id });

            var second = new BusGateway { Name = "Second bus", DocumentId = documentId };
            db.Set<BusGateway>().Add(second);
            await db.SaveChangesAsync();
            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute { BusGatewayId = second.Id, SubscriptionId = id });

            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Delete(id));

        // Reporting one holder per attempt turns clearing an integration into a guessing game.
        Assert.Contains("First bus", ex.Message);
        Assert.Contains("Second bus", ex.Message);
    }

    [Fact]
    public async Task An_integration_nothing_points_at_deletes_cleanly()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Unreferenced"), documentId, partnerId);

        // The guard has to stay narrow: if it over-reaches, nothing can ever be deleted and the
        // only way out is the database.
        await Delete(id);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<Subscription>().AnyAsync(s => s.Id == id));
    }

    [Fact]
    public async Task Exchange_history_does_not_keep_an_integration_alive()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Has history"), documentId, partnerId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == id);
            db.Set<Xchange>().Add(new Xchange(subscription, new XchangeFile("{\"done\":true}")));
            await db.SaveChangesAsync();
        }

        // Deliberate: an exchange's reference is nullable, and past traffic is not a reason to keep
        // configuration around forever.
        await Delete(id);

        await using var finalScope = _fixture.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await finalDb.Set<Subscription>().AnyAsync(s => s.Id == id));
    }

    [Fact]
    public async Task A_viewer_cannot_create_or_delete_an_integration()
    {
        var (documentId, partnerId) = await Groundwork();
        var id = await CreateSubscription(Unique("Guarded"), documentId, partnerId);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var viewer = new Domain.Accounts.Account("Viewer", Unique("sub-viewer") + "@test.local",
            "hash", Domain.Accounts.AccountRole.Viewer);
        db.Set<Domain.Accounts.Account>().Add(viewer);
        await db.SaveChangesAsync();
        db.Set<Domain.Accounts.AccountRoleLink>()
            .Add(new Domain.Accounts.AccountRoleLink(viewer.Id, Domain.Accounts.Role.ViewerId));
        await db.SaveChangesAsync();

        scope.As(viewer.Id);

        var create = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => create.Handle(new SubscriptionCreate
        {
            Name = "Should not exist",
            DocumentId = documentId,
            PartnerId = partnerId,
            Type = SubscriptionType.ApiCall,
        }));

        var delete = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Delete>(scope.ServiceProvider);
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => delete.Handle(id));
    }
}
