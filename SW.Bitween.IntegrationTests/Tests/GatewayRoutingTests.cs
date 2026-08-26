using System.Collections.Generic;
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
/// Which integrations run when a document arrives on the bus.
/// </summary>
/// <remarks>
/// <see cref="FilterService"/> is the dispatcher: one message lands, and it decides — from the
/// payload alone — which integrations are handed it. Everything downstream is a consequence of
/// what it returns, and until now nothing exercised it. Both directions matter equally here: an
/// integration that stops being selected goes quiet with no error anywhere, and one that is
/// selected when it shouldn't be runs real traffic through the wrong pipeline.
/// </remarks>
[Collection("Bitween")]
public class GatewayRoutingTests
{
    private readonly BitweenFixture _fixture;

    public GatewayRoutingTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>
    /// A JSON information type promoting the two fields the route filters read, with its id left
    /// to the database. Hand-picked ids are how tests in this shared collection collide: the
    /// number has to be unclaimed by every other test file, and nothing enforces that.
    /// </summary>
    private static async Task<int> OrdersDocument(BitweenDbContext db, string name)
    {
        var doc = new Document(0, Unique(name), DocumentFormat.Json);
        doc.SetDictionaries(new Dictionary<string, string>
        {
            ["country"] = "country",
            ["channel"] = "channel",
        });
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private static Subscription BusGatewayIntegration(string name, int documentId)
        => new(name, documentId, SubscriptionType.BusGateway) { Inactive = false };

    /// <summary>
    /// Runs the dispatcher over a payload. The cache is revoked first: it is a singleton holding a
    /// ten-minute snapshot, so without this a test reads configuration from before its own setup.
    /// </summary>
    private async Task<FilterResult> Dispatch(int documentId, string payload)
    {
        await using var scope = _fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        var filterService = scope.ServiceProvider.GetRequiredService<FilterService>();
        return await filterService.Filter(documentId, new XchangeFile(payload));
    }

    [Fact]
    public async Task A_route_with_no_filter_runs_its_integration_for_every_message()
    {
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var docId = await OrdersDocument(db, "Routing catch-all");

            var integration = BusGatewayIntegration(Unique("Catch-all handler"), docId);
            db.Set<Subscription>().Add(integration);
            var gateway = new BusGateway { Name = Unique("Catch-all bus"), DocumentId = docId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();

            // No MatchExpression at all — the "send me everything on this type" route.
            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute
            {
                BusGatewayId = gateway.Id,
                SubscriptionId = integration.Id,
            });
            await db.SaveChangesAsync();

            var result = await Dispatch(docId, "{\"country\":\"JO\",\"channel\":\"web\"}");
            Assert.Contains(result.GatewayHits, h => h.SubscriptionId == integration.Id);

            var other = await Dispatch(docId, "{\"country\":\"AE\",\"channel\":\"pos\"}");
            Assert.Contains(other.GatewayHits, h => h.SubscriptionId == integration.Id);
        }
    }

    [Fact]
    public async Task A_route_filter_selects_only_the_messages_it_names()
    {
        int docId, jordan, emirates;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing by country");

            var jordanIntegration = BusGatewayIntegration(Unique("Jordan pipeline"), docId);
            var emiratesIntegration = BusGatewayIntegration(Unique("Emirates pipeline"), docId);
            db.Set<Subscription>().AddRange(jordanIntegration, emiratesIntegration);
            var gateway = new BusGateway { Name = Unique("Country bus"), DocumentId = docId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();

            jordan = jordanIntegration.Id;
            emirates = emiratesIntegration.Id;

            db.Set<BusGatewayRoute>().AddRange(
                new BusGatewayRoute
                {
                    BusGatewayId = gateway.Id,
                    SubscriptionId = jordan,
                    MatchExpression = new OneOfSpec("country", ["JO"]),
                },
                new BusGatewayRoute
                {
                    BusGatewayId = gateway.Id,
                    SubscriptionId = emirates,
                    MatchExpression = new OneOfSpec("country", ["AE"]),
                });
            await db.SaveChangesAsync();
        }

        var result = await Dispatch(docId, "{\"country\":\"JO\",\"channel\":\"web\"}");

        // Selecting is the entire job — a filter that lets everything through is the same bug as
        // one that lets nothing through, and only checking both sides catches it.
        Assert.Contains(result.GatewayHits, h => h.SubscriptionId == jordan);
        Assert.DoesNotContain(result.GatewayHits, h => h.SubscriptionId == emirates);
    }

    [Fact]
    public async Task A_route_carries_its_partner_to_the_integration_it_runs()
    {
        int docId, integrationId, partnerId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing with partner");
            var partner = new Partner(Unique("Routed partner"));
            db.Set<Partner>().Add(partner);
            await db.SaveChangesAsync();

            var integration = BusGatewayIntegration(Unique("Partner pipeline"), docId);
            db.Set<Subscription>().Add(integration);
            var gateway = new BusGateway { Name = Unique("Partner bus"), DocumentId = docId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();

            integrationId = integration.Id;
            partnerId = partner.Id;

            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute
            {
                BusGatewayId = gateway.Id,
                SubscriptionId = integrationId,
                PartnerId = partnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Dispatch(docId, "{\"country\":\"JO\",\"channel\":\"web\"}");

        // A bus gateway integration has no partner of its own, so this is the only place the
        // partner can come from. Lose it here and every {{partner.…}} in its adapters stays a
        // literal token — the request goes out to a URL with the placeholder still in it.
        var hit = Assert.Single(result.GatewayHits, h => h.SubscriptionId == integrationId);
        Assert.Equal(partnerId, hit.PartnerId);
    }

    [Fact]
    public async Task A_deactivated_gateway_offers_none_of_its_routes()
    {
        int docId, integrationId, gatewayId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing deactivated");

            var integration = BusGatewayIntegration(Unique("Paused pipeline"), docId);
            db.Set<Subscription>().Add(integration);
            var gateway = new BusGateway { Name = Unique("Paused bus"), DocumentId = docId };
            db.Set<BusGateway>().Add(gateway);
            await db.SaveChangesAsync();

            integrationId = integration.Id;
            gatewayId = gateway.Id;

            db.Set<BusGatewayRoute>().Add(new BusGatewayRoute
            {
                BusGatewayId = gatewayId,
                SubscriptionId = integrationId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Contains((await Dispatch(docId, "{\"country\":\"JO\"}")).GatewayHits,
            h => h.SubscriptionId == integrationId);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<BusGateway>().SingleAsync(g => g.Id == gatewayId);
            gateway.Inactive = true;
            await db.SaveChangesAsync();
        }

        // This is all deactivating a bus gateway means: the message still publishes, this gateway
        // just stops being one of the places it lands. Turning it off is the only alternative to
        // deleting it, and deleting takes the routes with it.
        Assert.DoesNotContain((await Dispatch(docId, "{\"country\":\"JO\"}")).GatewayHits,
            h => h.SubscriptionId == integrationId);
    }

    [Fact]
    public async Task An_integration_with_its_own_entry_point_is_not_run_by_a_message_arriving()
    {
        int docId;
        var ids = new Dictionary<SubscriptionType, int>();

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing entry points");
            var partner = new Partner(Unique("Entry point partner"));
            db.Set<Partner>().Add(partner);
            await db.SaveChangesAsync();

            var subscriptions = new Dictionary<SubscriptionType, Subscription>
            {
                // Started by its gateway's routes.
                [SubscriptionType.BusGateway] =
                    new("Entry bus gateway", docId, SubscriptionType.BusGateway) { Inactive = false },
                // Started by a partner calling the gateway it is attached to.
                [SubscriptionType.GatewayApiCall] =
                    new("Entry gateway api", docId, SubscriptionType.GatewayApiCall) { Inactive = false },
                // Started by its schedule, through ReceivingJob.
                [SubscriptionType.Receiving] =
                    new("Entry receiving", docId) { Inactive = false },
                // Started by its own partner posting to Xchanges/Update.
                [SubscriptionType.ApiCall] =
                    new("Entry api call", docId, SubscriptionType.ApiCall, partner.Id) { Inactive = false },
                // The control. Without one, every assertion below would also pass if the
                // subscriptions simply never reached the dispatcher — an empty result looks
                // identical to a correctly filtered one.
                [SubscriptionType.Internal] =
                    new("Entry control", docId, SubscriptionType.Internal, partner.Id) { Inactive = false },
            };

            db.Set<Subscription>().AddRange(subscriptions.Values);
            await db.SaveChangesAsync();

            foreach (var (type, subscription) in subscriptions)
                ids[type] = subscription.Id;
        }

        var result = await Dispatch(docId, "{\"country\":\"JO\",\"channel\":\"web\"}");

        Assert.Contains(ids[SubscriptionType.Internal], result.Hits);

        // Each of these four is started by something that decides it should run at all. Matching
        // them here as well ran them a second time on traffic addressed to nobody: a scheduled job
        // publishing the very message type it is bound to fed itself forever, and an ApiCall
        // integration belonging to one partner ran on another partner's message. The second run
        // also arrived without the partner its entry point would have passed.
        foreach (var (type, id) in ids.Where(p => p.Key != SubscriptionType.Internal))
            Assert.DoesNotContain(id, result.Hits);
    }

    [Fact]
    public async Task An_internal_integration_still_runs_when_its_document_arrives()
    {
        int docId, matching, filteredOut;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing internal");
            var partner = new Partner(Unique("Internal partner"));
            db.Set<Partner>().Add(partner);
            await db.SaveChangesAsync();

            var open = new Subscription("Internal open", docId, SubscriptionType.Internal, partner.Id)
                { Inactive = false };
            var narrowed = new Subscription("Internal narrowed", docId, SubscriptionType.Internal, partner.Id)
                { Inactive = false };
            narrowed.SetMatchExpression(new OneOfSpec("channel", ["pos"]));

            db.Set<Subscription>().AddRange(open, narrowed);
            await db.SaveChangesAsync();

            matching = open.Id;
            filteredOut = narrowed.Id;
        }

        var result = await Dispatch(docId, "{\"country\":\"JO\",\"channel\":\"web\"}");

        // The counterweight to the test above: reacting to a document of its type arriving is the
        // whole definition of an Internal integration — it has no other trigger, so a guard that
        // over-reaches silences it with nothing to show for it.
        Assert.Contains(matching, result.Hits);
        Assert.DoesNotContain(filteredOut, result.Hits);
    }

    [Fact]
    public async Task The_promoted_properties_are_read_off_the_payload_as_sent()
    {
        int docId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            docId = await OrdersDocument(db, "Routing promoted properties");
        }

        var result = await Dispatch(docId, "{\"country\":\"Acme Retail\",\"channel\":\"web\"}");

        // Case is preserved. These values are what every screen displays, and lower-casing them
        // here to pair with a search term listed an order for "Acme Retail" as "acme retail".
        Assert.Equal("Acme Retail", result.Properties["country"]);
        Assert.Equal("web", result.Properties["channel"]);
    }
}
