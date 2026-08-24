using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// <c>{{partner.…}}</c> in an adapter's properties has to be replaced by the time the Xchange is
/// written, because that is the copy the handler runs on — nothing resolves it later.
/// </summary>
/// <remarks>
/// Every caller of <c>CreateXchange</c> that has a partner in hand (a bus gateway route, an API
/// gateway call) passed one, and every caller that didn't left the token literal — so a handler
/// posted to the URL <c>{{partner.webhookUrl}}</c>. The subscription's own partner was sitting
/// on the subscription the whole time; these tests pin down that it is now used.
/// </remarks>
[Collection("Bitween")]
public class PartnerTokenTests
{
    private readonly BitweenFixture _fixture;

    public PartnerTokenTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Subscription_own_partner_fills_handler_tokens()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var partner = new Partner("Token Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["merchantSlug"] = "acme" }
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var doc = new Document(6101, "Partner Token Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        // Internal: the type whose only trigger is a document of its type arriving, so it reaches
        // CreateXchange without a partner being handed in from outside.
        var sub = new Subscription("Token Sub", doc.Id, SubscriptionType.Internal, partner.Id);
        sub.Inactive = false;
        sub.SetDictionaries(
            new Dictionary<string, string> { ["Url"] = "http://host/{{partner.merchantSlug}}" },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var xchange = await xchangeService.CreateXchange(sub, new XchangeFile("{\"id\":1}"));
        await db.SaveChangesAsync();

        Assert.Equal("http://host/acme", xchange.HandlerProperties["Url"]);
        Assert.Equal(partner.Id, xchange.PartnerId);
    }

    [Fact]
    public async Task Partner_handed_in_wins_over_the_subscriptions_own()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var own = new Partner("Own Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["merchantSlug"] = "own" }
        };
        var routed = new Partner("Routed Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["merchantSlug"] = "routed" }
        };
        db.Set<Partner>().AddRange(own, routed);
        await db.SaveChangesAsync();

        var doc = new Document(6102, "Partner Token Doc 2");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("Token Sub 2", doc.Id, SubscriptionType.Internal, own.Id);
        sub.Inactive = false;
        sub.SetDictionaries(
            new Dictionary<string, string> { ["Url"] = "http://host/{{partner.merchantSlug}}" },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        // A bus gateway route's partner: the caller knows better than the subscription does,
        // so the fallback must not override it.
        var xchange = await xchangeService.CreateXchange(sub, new XchangeFile("{\"id\":2}"),
            gatewayPartner: routed);
        await db.SaveChangesAsync();

        Assert.Equal("http://host/routed", xchange.HandlerProperties["Url"]);
        Assert.Equal(routed.Id, xchange.PartnerId);
    }

    [Fact]
    public async Task No_partner_anywhere_leaves_the_token_alone()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var doc = new Document(6103, "Partner Token Doc 3");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        // A bus gateway subscription carries no partner of its own; a route may or may not
        // supply one. With none, there is nothing to resolve against and the token stands.
        var sub = new Subscription("Token Sub 3", doc.Id, SubscriptionType.BusGateway);
        sub.Inactive = false;
        sub.SetDictionaries(
            new Dictionary<string, string> { ["Url"] = "http://host/{{partner.merchantSlug}}" },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var xchange = await xchangeService.CreateXchange(sub, new XchangeFile("{\"id\":3}"));
        await db.SaveChangesAsync();

        Assert.Equal("http://host/{{partner.merchantSlug}}", xchange.HandlerProperties["Url"]);
        Assert.Null(xchange.PartnerId);
    }
}
