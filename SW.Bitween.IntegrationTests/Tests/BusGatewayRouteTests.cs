using System.Linq;
using System.Threading;
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
/// Wiring up a bus gateway's routes — the configuration side of what
/// <see cref="GatewayRoutingTests"/> exercises at run time.
/// </summary>
/// <remarks>
/// A bus gateway is bound to one information type and every route under it inherits that binding.
/// The rules here exist because the alternative is a route that saves cleanly and then never fires
/// — its integration reads a payload shaped like something else, matches nothing, and reports no
/// error at any point.
/// </remarks>
[Collection("Bitween")]
public class BusGatewayRouteTests
{
    private readonly BitweenFixture _fixture;

    public BusGatewayRouteTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> CreateDocument()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(0, Unique("Bus doc"));
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    private async Task<int> CreateGateway(int documentId)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);
        return (int)await handler.Handle(new BusGatewayCreate
            { Name = Unique("Bus gateway"), DocumentId = documentId });
    }

    private async Task<int> AddRoute(int gatewayId, BusGatewayRouteCreate model)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.AddRoute>(scope.ServiceProvider);
        return (int)await handler.Handle(gatewayId, model);
    }

    private async Task<int> CreateBusIntegration(int documentId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var subscription = new Subscription(Unique("Bus integration"), documentId, SubscriptionType.BusGateway);
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    [Fact]
    public async Task A_gateway_has_to_name_an_information_type_that_exists()
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider);

        // The binding is fixed at creation and every route inherits it, so a wrong one here is
        // not something a later edit can put right.
        await Assert.ThrowsAsync<SWNotFoundException>(() => handler.Handle(new BusGatewayCreate
            { Name = Unique("Orphan gateway"), DocumentId = 999_999 }));
    }

    [Fact]
    public async Task A_route_cannot_run_an_integration_bound_to_a_different_information_type()
    {
        var gatewayDocument = await CreateDocument();
        var otherDocument = await CreateDocument();
        var gatewayId = await CreateGateway(gatewayDocument);
        var mismatched = await CreateBusIntegration(otherDocument);

        // This is the failure worth catching at save time: the route saves, the message arrives,
        // the integration reads a payload shaped like something else and quietly does nothing.
        var ex = await Assert.ThrowsAsync<SWException>(() => AddRoute(gatewayId,
            new BusGatewayRouteCreate { SubscriptionId = mismatched }));
        Assert.Contains("same document", ex.Message);
    }

    [Fact]
    public async Task A_route_demands_an_integration_of_the_bus_gateway_kind()
    {
        var documentId = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);

        int wrongKind;
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            // Right information type, wrong trigger — this one is started by its own schedule.
            var receiving = new Subscription(Unique("Scheduled"), documentId);
            db.Set<Subscription>().Add(receiving);
            await db.SaveChangesAsync();
            wrongKind = receiving.Id;
        }

        var ex = await Assert.ThrowsAsync<SWException>(() => AddRoute(gatewayId,
            new BusGatewayRouteCreate { SubscriptionId = wrongKind }));
        Assert.Contains("BusGateway", ex.Message);
    }

    [Fact]
    public async Task A_route_naming_a_partner_that_does_not_exist_is_refused()
    {
        var documentId = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);
        var subscriptionId = await CreateBusIntegration(documentId);

        // The partner is where {{partner.…}} values come from at run time. A dangling id resolves
        // to nothing and every token stays a literal in the outgoing request.
        await Assert.ThrowsAsync<SWNotFoundException>(() => AddRoute(gatewayId,
            new BusGatewayRouteCreate { SubscriptionId = subscriptionId, PartnerId = 999_999 }));
    }

    [Fact]
    public async Task An_integration_defined_inline_takes_the_gateways_information_type()
    {
        var documentId = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);
        var integrationName = Unique("Inline route integration");

        await AddRoute(gatewayId, new BusGatewayRouteCreate
        {
            // DocumentId is deliberately wrong here. A bus gateway is bound to one information
            // type and imposes it, so the caller's answer is not consulted — which is also why
            // the mismatch this could otherwise cause is not reachable through this door.
            NewIntegration = new InlineIntegrationCreate { Name = integrationName, DocumentId = 999_999 },
        });

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var created = await db.Set<Subscription>().SingleAsync(s => s.Name == integrationName);

        Assert.Equal(documentId, created.DocumentId);
        Assert.Equal(SubscriptionType.BusGateway, created.Type);
        Assert.False(created.Inactive);
        Assert.True(await db.Set<BusGatewayRoute>()
            .AnyAsync(r => r.BusGatewayId == gatewayId && r.SubscriptionId == created.Id));
    }

    [Fact]
    public async Task Repointing_a_route_keeps_the_same_rules()
    {
        var documentId = await CreateDocument();
        var otherDocument = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);
        var original = await CreateBusIntegration(documentId);
        var replacement = await CreateBusIntegration(documentId);
        var mismatched = await CreateBusIntegration(otherDocument);

        var routeId = await AddRoute(gatewayId, new BusGatewayRouteCreate { SubscriptionId = original });

        async Task Update(BusGatewayRouteUpdate model)
        {
            await using var scope = _fixture.CreateScope();
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.UpdateRoute>(scope.ServiceProvider);
            await handler.Handle(gatewayId, model);
        }

        // The information type check has to hold on the edit path too — otherwise every rule
        // above is one save-and-edit away from being bypassed.
        await Assert.ThrowsAsync<SWException>(() => Update(new BusGatewayRouteUpdate
            { RouteId = routeId, SubscriptionId = mismatched }));

        // Repointing always names an integration that already exists; defining one inline
        // belongs to the route being created.
        var neither = await Assert.ThrowsAsync<SWValidationException>(() => Update(new BusGatewayRouteUpdate
            { RouteId = routeId, NewIntegration = new InlineIntegrationCreate { Name = "Too late" } }));
        Assert.StartsWith(GatewayLinkTarget.NeitherGiven, neither.Message);

        await Update(new BusGatewayRouteUpdate
        {
            RouteId = routeId,
            SubscriptionId = replacement,
            MatchExpression = new OneOfSpec("channel", ["pos"]),
        });

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var route = await db.Set<BusGatewayRoute>().SingleAsync(r => r.Id == routeId);

        Assert.Equal(replacement, route.SubscriptionId);
        Assert.Equal("channel is one of [pos]", route.MatchExpression.ToString());
    }

    [Fact]
    public async Task Deleting_a_gateway_takes_its_routes_with_it()
    {
        var documentId = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);
        var subscriptionId = await CreateBusIntegration(documentId);
        await AddRoute(gatewayId, new BusGatewayRouteCreate { SubscriptionId = subscriptionId });

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.Delete>(scope.ServiceProvider);
            await handler.Handle(gatewayId);
        }

        await using var check = _fixture.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<BusGateway>().AnyAsync(g => g.Id == gatewayId));
        Assert.False(await db.Set<BusGatewayRoute>().AnyAsync(r => r.BusGatewayId == gatewayId));

        // The route goes, the integration stays. It is configuration in its own right, and
        // deleting it here would be silent data loss dressed up as tidying.
        Assert.True(await db.Set<Subscription>().AnyAsync(s => s.Id == subscriptionId));
    }

    [Fact]
    public async Task Removing_one_route_leaves_the_gateways_others_alone()
    {
        var documentId = await CreateDocument();
        var gatewayId = await CreateGateway(documentId);
        var first = await AddRoute(gatewayId,
            new BusGatewayRouteCreate { SubscriptionId = await CreateBusIntegration(documentId) });
        var second = await AddRoute(gatewayId,
            new BusGatewayRouteCreate { SubscriptionId = await CreateBusIntegration(documentId) });

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.BusGateways.RemoveRoute>(scope.ServiceProvider);
            await handler.Handle(gatewayId, new RemoveRouteRequest { RouteId = first });
        }

        await using var check = _fixture.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<BusGatewayRoute>().AnyAsync(r => r.Id == first));
        Assert.True(await db.Set<BusGatewayRoute>().AnyAsync(r => r.Id == second));
    }
}
