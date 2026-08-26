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
/// The gateway a partner calls, and the attachments that decide what their call runs.
/// </summary>
/// <remarks>
/// An API gateway is a URL handed to an outside company, and an attachment is the rule that says
/// "when this partner calls it, run that integration". Both halves fail quietly when they are
/// wrong: a url name that cannot appear in a path saves fine and 404s only when the partner
/// finally tries it, and an attachment pointing at the wrong kind of integration looks configured
/// from every screen.
/// </remarks>
[Collection("Bitween")]
public class ApiGatewayTests
{
    private readonly BitweenFixture _fixture;

    public ApiGatewayTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> CreateGateway(string urlName)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);
        return (int)await handler.Handle(new ApiGatewayCreate { Name = Unique("Gateway"), UrlName = urlName });
    }

    private async Task AddPartner(int gatewayId, ApiGatewayPartnerCreate model)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.AddPartner>(scope.ServiceProvider);
        await handler.Handle(gatewayId, model);
    }

    /// <summary>A partner, an information type, and an integration of the type attachments demand.</summary>
    private async Task<(int partnerId, int documentId, int subscriptionId)> Groundwork()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner(Unique("Gateway partner"));
        db.Set<Partner>().Add(partner);
        var document = new Document(null, Unique("Gateway doc"), DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Gateway integration"), document.Id,
            SubscriptionType.GatewayApiCall);
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        return (partner.Id, document.Id, subscription.Id);
    }

    [Theory]
    [InlineData("order sync")]      // the one that actually happens — a space
    [InlineData("Order-Sync")]      // upper case, which the route match is not
    [InlineData("orders/sync")]     // a second path segment
    [InlineData("-orders")]
    public async Task A_url_name_that_cannot_appear_in_a_path_is_refused(string urlName)
    {
        // Partners call /api/Gateway/{urlName}/sync. Anything needing escaping there produces a
        // gateway that reads as configured on every screen and cannot be reached — and the URL
        // the partner is given to copy is the broken one.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => CreateGateway(urlName));
        Assert.StartsWith("GATEWAY_URL_NAME_INVALID", ex.Message);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("order-sync")]
    [InlineData("order_sync_v2")]
    [InlineData("orders2")]
    public async Task A_usable_url_name_is_accepted(string urlName)
    {
        // The guard has to stay narrow: refusing a legitimate name blocks a gateway from
        // existing at all, with the error pointing at the name rather than the rule.
        var id = await CreateGateway(urlName);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task Attaching_a_partner_demands_an_integration_of_the_gateway_kind()
    {
        var (partnerId, documentId, _) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());

        int wrongKindId;
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            // A perfectly good integration — of a kind that is started by its own schedule, not
            // by a partner calling in.
            var receiving = new Subscription(Unique("Scheduled"), documentId);
            db.Set<Subscription>().Add(receiving);
            await db.SaveChangesAsync();
            wrongKindId = receiving.Id;
        }

        var ex = await Assert.ThrowsAsync<SWException>(() => AddPartner(gatewayId,
            new ApiGatewayPartnerCreate { PartnerId = partnerId, SubscriptionId = wrongKindId }));
        Assert.Contains("GatewayApiCall", ex.Message);
    }

    [Fact]
    public async Task The_same_partner_and_integration_cannot_be_attached_twice()
    {
        var (partnerId, _, subscriptionId) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());

        var attachment = new ApiGatewayPartnerCreate { PartnerId = partnerId, SubscriptionId = subscriptionId };
        await AddPartner(gatewayId, attachment);

        // Without this the second attachment wins silently and the first is unreachable — two
        // rows on screen where only one can ever run.
        await Assert.ThrowsAsync<SWException>(() => AddPartner(gatewayId, attachment));
    }

    [Fact]
    public async Task Deleting_a_gateway_takes_its_attachments_with_it()
    {
        var (partnerId, _, subscriptionId) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());
        await AddPartner(gatewayId, new ApiGatewayPartnerCreate
            { PartnerId = partnerId, SubscriptionId = subscriptionId });

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Delete>(scope.ServiceProvider);
            await handler.Handle(gatewayId);
        }

        await using var check = _fixture.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<ApiGateway>().AnyAsync(g => g.Id == gatewayId));
        Assert.False(await db.Set<ApiGatewayPartner>().AnyAsync(p => p.ApiGatewayId == gatewayId));

        // The integration itself is configuration in its own right and outlives the gateway —
        // cascading into it would delete work the attachment merely referenced.
        Assert.True(await db.Set<Subscription>().AnyAsync(s => s.Id == subscriptionId));
    }

    [Fact]
    public async Task An_attachment_names_exactly_one_integration()
    {
        var (partnerId, documentId, subscriptionId) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());

        var both = await Assert.ThrowsAsync<SWValidationException>(() => AddPartner(gatewayId,
            new ApiGatewayPartnerCreate
            {
                PartnerId = partnerId,
                SubscriptionId = subscriptionId,
                NewIntegration = new InlineIntegrationCreate { Name = "Ambiguous", DocumentId = documentId },
            }));
        Assert.StartsWith(GatewayLinkTarget.BothGiven, both.Message);

        var neither = await Assert.ThrowsAsync<SWValidationException>(() => AddPartner(gatewayId,
            new ApiGatewayPartnerCreate { PartnerId = partnerId }));
        Assert.StartsWith(GatewayLinkTarget.NeitherGiven, neither.Message);
    }

    [Fact]
    public async Task An_integration_defined_inline_lands_with_its_attachment_or_not_at_all()
    {
        var (partnerId, documentId, _) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());
        var integrationName = Unique("Defined inline");

        await AddPartner(gatewayId, new ApiGatewayPartnerCreate
        {
            PartnerId = partnerId,
            NewIntegration = new InlineIntegrationCreate { Name = integrationName, DocumentId = documentId },
        });

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var created = await db.Set<Subscription>().SingleAsync(s => s.Name == integrationName);

        Assert.Equal(SubscriptionType.GatewayApiCall, created.Type);

        // Live the moment it exists, unlike an ordinary create: it has no trigger of its own, so
        // the attachment made in the same transaction is the only thing that can ever run it.
        Assert.False(created.Inactive);
        Assert.True(await db.Set<ApiGatewayPartner>()
            .AnyAsync(p => p.ApiGatewayId == gatewayId && p.SubscriptionId == created.Id));
    }

    [Fact]
    public async Task An_inline_integration_that_fails_validation_leaves_nothing_behind()
    {
        var (partnerId, documentId, _) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());
        var integrationName = Unique("Never committed");

        // A bus message name with a space in it becomes a RabbitMQ routing key nothing can
        // answer. This door used to skip the checks the ordinary create applies.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => AddPartner(gatewayId,
            new ApiGatewayPartnerCreate
            {
                PartnerId = partnerId,
                NewIntegration = new InlineIntegrationCreate
                {
                    Name = integrationName,
                    DocumentId = documentId,
                    ResponseMessageTypeName = "Order Placed",
                },
            }));
        Assert.StartsWith("INVALID_BUS_TYPE_NAME", ex.Message);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Both rows go in on one save, so a refusal cannot leave a half-made integration that
        // nothing points at and nobody goes looking for.
        Assert.False(await db.Set<Subscription>().AnyAsync(s => s.Name == integrationName));
        Assert.False(await db.Set<ApiGatewayPartner>().AnyAsync(p => p.ApiGatewayId == gatewayId));
    }

    [Fact]
    public async Task An_inline_gateway_integration_cannot_carry_its_own_partner()
    {
        var (partnerId, documentId, _) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());

        // The partner reaches a gateway integration through the attachment — which is the very
        // thing being made here. One on the integration too is a second, disagreeing answer to
        // the same question.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => AddPartner(gatewayId,
            new ApiGatewayPartnerCreate
            {
                PartnerId = partnerId,
                NewIntegration = new InlineIntegrationCreate
                {
                    Name = Unique("Own partner"),
                    DocumentId = documentId,
                    PartnerId = partnerId,
                },
            }));
        Assert.StartsWith("PARTNER_NOT_ALLOWED", ex.Message);
    }
}
