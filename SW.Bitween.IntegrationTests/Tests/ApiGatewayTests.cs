using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Controllers;
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
public class ApiGatewayTests(BitweenFixture fixture)
{
    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> CreateGateway(string urlName)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);
        return (int)await handler.Handle(new ApiGatewayCreate { Name = Unique("Gateway"), UrlName = urlName });
    }

    private async Task AddPartner(int gatewayId, ApiGatewayPartnerCreate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.AddPartner>(scope.ServiceProvider);
        await handler.Handle(gatewayId, model);
    }

    /// <summary>A partner, an information type, and an integration of the type attachments demand.</summary>
    private async Task<(int partnerId, int documentId, int subscriptionId)> Groundwork()
    {
        await using var scope = fixture.CreateScope();
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

    /// <summary>A gateway with one partner attached, holding one key named <c>orders-prod</c>.</summary>
    private async Task<(string urlName, string key)> GatewayWithKey()
    {
        var urlName = Unique("keyed").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Key holder"));
        partner.SetApiCredentials([new ApiCredential("orders-prod", key)]);
        var doc = new Document(null, Unique("Keyed doc"), DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Keyed sub"), doc.Id, SubscriptionType.GatewayApiCall)
            { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = Unique("Keyed gateway"),
            UrlName = urlName,
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        return (urlName, key);
    }

    private async Task<IActionResult> CallGateway(string urlName, string header, string value)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
            new ClaimsPrincipal(new ClaimsIdentity()),
            [new RequestValue(header, value, RequestValueType.HttpHeader)]);

        var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        return await controller.Post($"{urlName}/async");
    }

    private static string Basic(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    [Theory]
    [InlineData("partnerkey")]
    [InlineData("bearer")]
    [InlineData("basic")]
    public async Task A_partner_key_is_accepted_in_any_of_the_three_places(string how)
    {
        // Many clients can only fill in a bearer token or a username and password, and cannot
        // add a header of our own naming. It is the same key either way, so it opens the same door.
        var (urlName, key) = await GatewayWithKey();
        var (header, value) = how switch
        {
            "partnerkey" => ("partnerkey", key),
            "bearer" => ("Authorization", $"Bearer {key}"),
            _ => ("Authorization", Basic("orders-prod", key)),
        };

        Assert.IsType<AcceptedResult>(await CallGateway(urlName, header, value));
    }

    [Theory]
    [InlineData("bearer-wrong-key")]
    [InlineData("bearer-empty")]
    [InlineData("basic-wrong-username")]
    [InlineData("basic-key-as-username")]
    [InlineData("basic-not-base64")]
    [InlineData("other-scheme")]
    public async Task A_partner_key_sent_any_other_way_is_refused(string how)
    {
        var (urlName, key) = await GatewayWithKey();
        var value = how switch
        {
            "bearer-wrong-key" => $"Bearer {Guid.NewGuid():N}",
            "bearer-empty" => "Bearer ",
            // The password alone would find the partner, but a username naming some other key
            // means the client is set up wrong — better refused now than accepted by accident.
            "basic-wrong-username" => Basic("orders-test", key),
            "basic-key-as-username" => Basic(key, ""),
            "basic-not-base64" => "Basic not*base64",
            _ => $"Digest {key}",
        };

        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization", value));
    }

    /// <summary>Runs <paramref name="test"/> with the system-wide key header set, then puts it back.</summary>
    private async Task WithKeyHeader(string header, Func<Task> test)
    {
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        var was = options.PartnerKeyHeader;
        options.PartnerKeyHeader = header;
        try
        {
            await test();
        }
        finally
        {
            options.PartnerKeyHeader = was;
        }
    }

    [Fact]
    public async Task The_key_header_named_in_settings_takes_the_key_and_partnerkey_still_does()
    {
        var (urlName, key) = await GatewayWithKey();

        await WithKeyHeader("X-Api-Key", async () =>
        {
            Assert.IsType<AcceptedResult>(await CallGateway(urlName, "x-api-key", key));   // header names ignore case
            // Renaming the header mustn't cut off partners still sending the old one.
            Assert.IsType<AcceptedResult>(await CallGateway(urlName, "partnerkey", key));
            Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "X-Other-Key", key));
        });
    }

    [Fact]
    public async Task A_gateway_can_name_its_own_key_header()
    {
        var (urlName, key) = await GatewayWithKey();
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<ApiGateway>().SingleAsync(g => g.UrlName == urlName);
            gateway.PartnerKeyHeader = "X-Orders-Key";
            await db.SaveChangesAsync();
        }

        await WithKeyHeader("X-Api-Key", async () =>
        {
            Assert.IsType<AcceptedResult>(await CallGateway(urlName, "X-Orders-Key", key));
            Assert.IsType<AcceptedResult>(await CallGateway(urlName, "partnerkey", key));
            // The gateway's own name replaces the system-wide one rather than adding to it.
            Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "X-Api-Key", key));
        });
    }

    [Theory]
    [InlineData("Authorization")]   // already carries Bearer and Basic
    [InlineData("Wait-Period")]     // the gateway reads it for sync calls
    [InlineData("x api key")]
    public async Task A_key_header_that_would_be_read_as_something_else_is_refused(string header)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => handler.Handle(new ApiGatewayCreate
        {
            Name = Unique("Header"),
            UrlName = Unique("header").ToLowerInvariant(),
            Authentication = new ApiGatewayAuthentication { Method = GatewayAuthMethod.PartnerKey, KeyHeader = header },
        }));
        Assert.StartsWith("GATEWAY_KEY_HEADER_INVALID", ex.Message);
    }

    /// <summary>
    /// A gateway that trusts the test login server, with one partner attached whose identity
    /// there is returned — plus a second partner with an identity of its own, not attached.
    /// </summary>
    private async Task<(string urlName, string identity, string strangerIdentity)> JwtGateway()
    {
        var urlName = Unique("jwt").ToLowerInvariant();
        var identity = Unique("acme-orders");
        var strangerIdentity = Unique("not-attached");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Token holder")) { LoginIdentity = identity };
        var stranger = new Partner(Unique("Stranger")) { LoginIdentity = strangerIdentity };
        var doc = new Document(null, Unique("Jwt doc"), DocumentFormat.Json);
        db.AddRange(partner, stranger, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Jwt sub"), doc.Id, SubscriptionType.GatewayApiCall)
            { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = Unique("Jwt gateway"),
            UrlName = urlName,
            AuthMethod = GatewayAuthMethod.Jwt,
            JwtIssuer = TestLoginServer.Issuer,
            JwtAudience = "bitween",
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        return (urlName, identity, strangerIdentity);
    }

    [Fact]
    public async Task A_jwt_gateway_takes_a_token_from_its_login_server_as_the_partner_it_names()
    {
        var (urlName, identity, _) = await JwtGateway();

        var accepted = Assert.IsType<AcceptedResult>(await CallGateway(urlName, "Authorization",
            $"Bearer {fixture.LoginServer.Token(identity)}"));

        // The exchange records who called, as it records a key's name for a key.
        await using var scope = fixture.CreateScope();
        var xchange = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == (string)accepted.Location);
        Assert.Contains($"jwt: {identity}", xchange.References);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("for-another-system")]
    [InlineData("from-another-login-server")]
    [InlineData("forged")]
    [InlineData("unknown-identity")]
    [InlineData("partner-not-attached")]
    public async Task A_jwt_gateway_refuses_any_other_token(string how)
    {
        var (urlName, identity, strangerIdentity) = await JwtGateway();
        var login = fixture.LoginServer;
        var token = how switch
        {
            "expired" => login.Token(identity, expires: DateTime.UtcNow.AddMinutes(-10)),
            // Signed by the right login server, but issued for some other system — the audience
            // is the only thing that keeps it out.
            "for-another-system" => login.Token(identity, audience: "payroll"),
            "from-another-login-server" => login.Token(identity, issuer: "https://login.elsewhere.test"),
            "forged" => login.Token(identity, forged: true),
            "unknown-identity" => login.Token(Unique("nobody")),
            _ => login.Token(strangerIdentity),
        };

        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization", $"Bearer {token}"));
    }

    [Fact]
    public async Task A_login_server_that_cannot_be_reached_refuses_the_call_rather_than_failing_it()
    {
        // Without its keys no token can be checked, which is a refusal like any other: the caller
        // gets a 401, not a 500 carrying the reason.
        var (urlName, identity, _) = await JwtGateway();
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<ApiGateway>().SingleAsync(g => g.UrlName == urlName);
            gateway.JwtIssuer = TestLoginServer.UnreachableIssuer;
            await db.SaveChangesAsync();
        }

        var token = fixture.LoginServer.Token(identity, issuer: TestLoginServer.UnreachableIssuer);
        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization", $"Bearer {token}"));
    }

    [Fact]
    public async Task A_jwt_gateway_with_no_login_server_refuses_the_call_rather_than_failing_it()
    {
        // The API never saves one, but a row edited by hand can be JWT with no login server.
        var (urlName, identity, _) = await JwtGateway();
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var gateway = await db.Set<ApiGateway>().SingleAsync(g => g.UrlName == urlName);
            gateway.JwtIssuer = null;
            await db.SaveChangesAsync();
        }

        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization",
            $"Bearer {fixture.LoginServer.Token(identity)}"));
    }

    [Fact]
    public async Task A_jwt_gateway_refuses_partner_keys()
    {
        // The gateway says which way in it speaks. Taking keys too would leave the old door open
        // on a gateway someone moved to tokens precisely to close it.
        var (urlName, identity, _) = await JwtGateway();
        var key = Guid.NewGuid().ToString("N");
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = await db.Set<Partner>().SingleAsync(p => p.LoginIdentity == identity);
            partner.SetApiCredentials([new ApiCredential("orders-prod", key)]);
            await db.SaveChangesAsync();
        }

        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "partnerkey", key));
        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization", $"Bearer {key}"));
    }

    [Fact]
    public async Task A_partner_key_gateway_does_not_take_tokens()
    {
        var (urlName, _) = await GatewayWithKey();
        var token = fixture.LoginServer.Token(Unique("acme-orders"));

        Assert.IsType<UnauthorizedResult>(await CallGateway(urlName, "Authorization", $"Bearer {token}"));
    }

    [Theory]
    [InlineData("http://login.example.com", "bitween", "GATEWAY_JWT_ISSUER_INVALID")]   // keys could be swapped in transit
    [InlineData("login.example.com", "bitween", "GATEWAY_JWT_ISSUER_INVALID")]
    [InlineData("https://login.example.com", " ", "GATEWAY_JWT_AUDIENCE_REQUIRED")]
    public async Task A_jwt_gateway_that_could_never_work_or_trusts_too_much_is_refused(
        string issuer, string audience, string error)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Create>(scope.ServiceProvider);

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => handler.Handle(new ApiGatewayCreate
        {
            Name = Unique("Jwt"),
            UrlName = Unique("jwt").ToLowerInvariant(),
            Authentication = new ApiGatewayAuthentication
                { Method = GatewayAuthMethod.Jwt, Issuer = issuer, Audience = audience },
        }));
        Assert.StartsWith(error, ex.Message);
    }

    [Fact]
    public async Task Saving_a_gateway_without_its_authentication_keeps_what_it_has()
    {
        // Pausing a gateway saves it with only its name, url name and on/off. Reading "no
        // authentication" as "partner keys" would quietly reopen a JWT gateway to keys.
        var (urlName, _, _) = await JwtGateway();
        int id;
        await using (var scope = fixture.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<ApiGateway>().Where(g => g.UrlName == urlName).Select(g => g.Id).SingleAsync();

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Update>(scope.ServiceProvider);
            await handler.Handle(id, new ApiGatewayUpdate { Name = Unique("Paused"), UrlName = urlName, Inactive = true });
        }

        await using var check = fixture.CreateScope();
        var gateway = await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<ApiGateway>().AsNoTracking().SingleAsync(g => g.Id == id);
        Assert.Equal(GatewayAuthMethod.Jwt, gateway.AuthMethod);
        Assert.Equal(TestLoginServer.Issuer, gateway.JwtIssuer);
    }

    [Fact]
    public async Task Two_partners_cannot_share_a_login_identity()
    {
        // A JWT gateway finds the partner by this value alone.
        var identity = Unique("shared");
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.Partners.Create>(scope.ServiceProvider);
        await create.Handle(new PartnerCreate { Name = Unique("First"), LoginIdentity = identity });

        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            create.Handle(new PartnerCreate { Name = Unique("Second"), LoginIdentity = $"  {identity} " }));
        Assert.StartsWith("LOGIN_IDENTITY_TAKEN", ex.Message);
    }

    [Theory]
    [InlineData("order sync")]      // the one that actually happens — a space
    [InlineData("Order-Sync")]      // upper case, which the route match is not
    [InlineData("-orders")]
    [InlineData("/orders")]         // an empty part, front or back or middle
    [InlineData("orders/")]
    [InlineData("logistics//orders")]
    [InlineData("orders/sync")]     // ends where /sync or /async goes
    [InlineData("orders/async")]
    [InlineData("orders\n")]       // $ would let a final newline through
    public async Task A_url_name_that_cannot_appear_in_a_path_is_refused(string urlName)
    {
        // Partners call /api/gateway/{urlName}/sync. Anything needing escaping there produces a
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
    [InlineData("logistics/slim/orders")]   // a client's own scheme, as many parts as it has
    [InlineData("sync/orders")]             // only the last part is reserved
    public async Task A_usable_url_name_is_accepted(string urlName)
    {
        // The guard has to stay narrow: refusing a legitimate name blocks a gateway from
        // existing at all, with the error pointing at the name rather than the rule.
        var id = await CreateGateway(urlName);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task A_url_name_matching_an_older_mixed_case_one_is_taken()
    {
        // Rows saved before names had to be lowercase can still hold capitals, and partner calls
        // match without regard to case — so the lowercase twin would answer on the same address.
        var legacy = Unique("Legacy-Orders");
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Set<ApiGateway>().Add(new ApiGateway { Name = Unique("Legacy"), UrlName = legacy });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => CreateGateway(legacy.ToLowerInvariant()));
        Assert.StartsWith("GATEWAY_URL_NAME_TAKEN", ex.Message);
    }

    [Fact]
    public async Task Attaching_a_partner_demands_an_integration_of_the_gateway_kind()
    {
        var (partnerId, documentId, _) = await Groundwork();
        var gatewayId = await CreateGateway(Unique("gw").ToLowerInvariant());

        int wrongKindId;
        await using (var scope = fixture.CreateScope())
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

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var handler = ActivatorUtilities.CreateInstance<Resources.ApiGateways.Delete>(scope.ServiceProvider);
            await handler.Handle(gatewayId);
        }

        await using var check = fixture.CreateScope();
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

        await using var scope = fixture.CreateScope();
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

        await using var scope = fixture.CreateScope();
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
