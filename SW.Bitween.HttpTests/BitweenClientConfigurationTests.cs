using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Client;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// SimplyWorks.Bitween.Client's configuration areas and its gateway sender against a real Bitween:
/// what a program needs to set an integration up for each of its own customers, run it and watch it,
/// without anyone opening the admin UI.
/// </summary>
[Collection("Http")]
public class BitweenClientConfigurationTests(HttpFixture fixture)
{
    static readonly string[] Configure =
    [
        "partners.view", "partners.create", "partners.edit", "partners.delete",
        "subscriptions.view", "subscriptions.create", "subscriptions.edit", "subscriptions.delete", "subscriptions.operate",
        "documents.view",
        "api-gateways.view", "api-gateways.create", "api-gateways.edit", "api-gateways.delete",
        "bus-gateways.view", "bus-gateways.create", "bus-gateways.edit", "bus-gateways.delete",
        "notifiers.view", "notifiers.create", "notifiers.edit", "notifiers.delete",
        "exchanges.view",
    ];

    async Task<BitweenClient> ClientAsync(params string[] permissions)
    {
        using var admin = await fixture.AdminAsync();
        var roleId = await Api.CreateAsync(admin, "/api/roles", new { name = Api.Unique("config role"), permissions });
        var accountId = await Api.CreateAsync(admin, "/api/serviceaccounts", new { name = Api.Unique("config"), roleIds = new[] { roleId } });
        var issued = await Api.Json(await admin.PostAsJsonAsync($"/api/serviceaccounts/{accountId}/issueToken",
            new { name = "config", expiresInDays = 30 }));
        return new BitweenClient(fixture.Client(), (string)issued["token"]!);
    }

    async Task<int> InformationTypeAsync()
    {
        using var admin = await fixture.AdminAsync();
        return await Api.CreateAsync(admin, "/api/documents", new { name = Api.Unique("Invoice"), documentFormat = "Json" });
    }

    [Fact]
    public async Task Sets_a_partner_up_with_secrets_and_a_key_and_saves_it_back_unharmed()
    {
        using var bitween = await ClientAsync(Configure);
        var name = Api.Unique("Customer");
        var id = await bitween.Partners.CreateAsync(new PartnerCreate
        {
            Name = name,
            AdapterProperties = new Dictionary<string, string> { ["apiToken"] = "tok-123456", ["companyId"] = "42" },
            SecretProperties = ["apiToken"],
        });

        var partner = (await bitween.Partners.GetAsync(id))!;
        Assert.Equal(name, partner.Name);
        Assert.Equal("42", partner.AdapterProperties!["companyId"]);
        Assert.NotEqual("tok-123456", partner.AdapterProperties["apiToken"]);

        // A key, then the whole partner read and saved back with a changed setting: the masked secret and
        // the masked key it carries keep what they stand for.
        var key = await bitween.Partners.NewKeyAsync();
        partner.ApiCredentials = [new KeyAndValue { Key = "primary", Value = key }];
        await bitween.Partners.UpdateAsync(id, partner);
        partner = (await bitween.Partners.GetAsync(id))!;
        Assert.DoesNotContain(key, partner.ApiCredentials.Single().Value);
        partner.AdapterProperties!["companyId"] = "43";
        await bitween.Partners.UpdateAsync(id, partner);

        var stored = await fixture.InDbAsync(db => db.Set<Domain.Partner>().AsNoTracking().SingleAsync(p => p.Id == id));
        Assert.Equal("tok-123456", stored.AdapterProperties["apiToken"]);
        Assert.Equal("43", stored.AdapterProperties["companyId"]);

        var found = await bitween.Partners.SearchAsync(new ListQuery { NameContains = name[2..] });
        Assert.Equal(id, Assert.Single(found.Result).Id);
        Assert.Contains("apiToken", found.Result[0].PropertyKeys);

        await bitween.Partners.DeleteAsync(id);
        Assert.Null(await bitween.Partners.GetAsync(id));
    }

    [Fact]
    public async Task Creates_a_subscription_switches_it_on_and_off_and_pauses_it()
    {
        using var bitween = await ClientAsync(Configure);
        var informationTypeId = await InformationTypeAsync();
        var name = Api.Unique("Send invoices");
        var id = await bitween.Subscriptions.CreateAsync(new SubscriptionCreate
        {
            Name = name, DocumentId = informationTypeId, Type = SubscriptionType.GatewayApiCall,
        });

        var created = (await bitween.Subscriptions.GetAsync(id))!;
        Assert.True(created.Inactive);
        Assert.Equal(SubscriptionType.GatewayApiCall, created.Type);

        Assert.True(await bitween.Subscriptions.ActivateAsync(id));
        Assert.False(await bitween.Subscriptions.ActivateAsync(id));
        Assert.False((await bitween.Subscriptions.GetAsync(id))!.Inactive);

        Assert.True(await bitween.Subscriptions.PauseAsync(id));
        Assert.False(await bitween.Subscriptions.PauseAsync(id));
        Assert.NotNull((await bitween.Subscriptions.GetAsync(id))!.PausedOn);
        Assert.True(await bitween.Subscriptions.ResumeAsync(id));
        Assert.Null((await bitween.Subscriptions.GetAsync(id))!.PausedOn);

        var found = await bitween.Subscriptions.SearchAsync(new SubscriptionQuery
        {
            InformationTypeId = informationTypeId, Type = SubscriptionType.GatewayApiCall,
        });
        Assert.Equal(id, Assert.Single(found.Result).Id);
        Assert.Empty((await bitween.Subscriptions.SearchAsync(new SubscriptionQuery
        {
            InformationTypeId = informationTypeId, Type = SubscriptionType.BusGateway,
        })).Result);

        Assert.True(await bitween.Subscriptions.DeactivateAsync(id));
        await bitween.Subscriptions.DeleteAsync(id);
        Assert.Null(await bitween.Subscriptions.GetAsync(id));
    }

    [Fact]
    public async Task Routes_a_bus_gateways_messages_per_partner_with_a_match_expression()
    {
        using var bitween = await ClientAsync(Configure);
        var informationTypeId = await InformationTypeAsync();
        Assert.NotNull(await bitween.InformationTypes.GetAsync(informationTypeId));
        var partnerId = await bitween.Partners.CreateAsync(new PartnerCreate { Name = Api.Unique("Customer") });
        var subscriptionId = await bitween.Subscriptions.CreateAsync(new SubscriptionCreate
        {
            Name = Api.Unique("Bus invoices"), DocumentId = informationTypeId, Type = SubscriptionType.BusGateway, Inactive = false,
        });
        var gatewayId = await bitween.BusGateways.CreateAsync(new BusGatewayCreate
        {
            Name = Api.Unique("Invoices issued"), DocumentId = informationTypeId,
        });

        var routeId = await bitween.BusGateways.AddRouteAsync(gatewayId, new BusGatewayRouteCreate
        {
            SubscriptionId = subscriptionId,
            PartnerId = partnerId,
            MatchExpression = new AndSpec(new OneOfSpec("issuer.id", ["7", "8"]), new NotOneOfSpec("kind", ["draft"])),
        });

        var route = Assert.Single((await bitween.BusGateways.GetAsync(gatewayId))!.Routes);
        Assert.Equal(routeId, route.Id);
        Assert.Equal(partnerId, route.PartnerId);
        var and = Assert.IsType<AndSpec>(route.MatchExpression);
        var oneOf = Assert.IsType<OneOfSpec>(and.Left);
        Assert.Equal("issuer.id", oneOf.Path);
        Assert.Equal(["7", "8"], oneOf.Values);
        Assert.Equal(["draft"], Assert.IsType<NotOneOfSpec>(and.Right).Values);

        await bitween.BusGateways.UpdateRouteAsync(gatewayId, new BusGatewayRouteUpdate
        {
            RouteId = routeId, SubscriptionId = subscriptionId, PartnerId = partnerId,
            MatchExpression = new OrSpec(new OneOfSpec("issuer.id", ["9"]), new OneOfSpec("issuer.id", ["10"])),
        });
        Assert.IsType<OrSpec>(Assert.Single((await bitween.BusGateways.GetAsync(gatewayId))!.Routes).MatchExpression);

        await bitween.BusGateways.RemoveRouteAsync(gatewayId, routeId);
        Assert.Empty((await bitween.BusGateways.GetAsync(gatewayId))!.Routes);
        await bitween.BusGateways.DeleteAsync(gatewayId);
        Assert.Null(await bitween.BusGateways.GetAsync(gatewayId));
    }

    [Fact]
    public async Task Keeps_a_notification_channels_secret_and_says_why_a_test_send_failed()
    {
        using var bitween = await ClientAsync(Configure);
        var name = Api.Unique("Results");
        var properties = new Dictionary<string, string>
        {
            ["Host"] = "smtp.invalid", ["Port"] = "587", ["UseTls"] = "true", ["Password"] = "mail-secret",
            ["From"] = "bitween@example.com", ["To"] = "ops@example.com",
            ["Subject"] = "{{ SubscriptionName }} finished", ["Body"] = "Exchange {{ Id }}",
        };
        var id = await bitween.NotificationChannels.CreateAsync(new NotificationChannelCreate
        {
            Name = name, HandlerId = "NativeSmtpHandler", HandlerProperties = properties,
        });

        var channel = (await bitween.NotificationChannels.GetAsync(id))!;
        Assert.NotEqual("mail-secret", channel.HandlerProperties["Password"]);
        channel.HandlerProperties["To"] = "team@example.com";
        await bitween.NotificationChannels.UpdateAsync(id, channel);
        var stored = await fixture.InDbAsync(db => db.Set<Domain.NotificationChannel>().AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal("mail-secret", stored.HandlerProperties["Password"]);
        Assert.Equal("team@example.com", stored.HandlerProperties["To"]);

        var sent = await bitween.NotificationChannels.TestAsync(id);
        Assert.False(sent.Sent);
        Assert.False(string.IsNullOrWhiteSpace(sent.Error));

        Assert.Equal(id, Assert.Single((await bitween.NotificationChannels.SearchAsync(new ListQuery { NameContains = name })).Result).Id);
        await bitween.NotificationChannels.DeleteAsync(id);
        Assert.Null(await bitween.NotificationChannels.GetAsync(id));
    }

    [Fact]
    public async Task Attaches_partners_to_an_api_gateway_and_sends_as_one_of_them()
    {
        using var bitween = await ClientAsync(Configure);
        var informationTypeId = await InformationTypeAsync();
        var subscriptionId = await bitween.Subscriptions.CreateAsync(new SubscriptionCreate
        {
            Name = Api.Unique("Gateway invoices"), DocumentId = informationTypeId, Type = SubscriptionType.GatewayApiCall, Inactive = false,
        });
        var key = await bitween.Partners.NewKeyAsync();
        var partnerId = await bitween.Partners.CreateAsync(new PartnerCreate { Name = Api.Unique("Customer") });
        var partner = (await bitween.Partners.GetAsync(partnerId))!;
        partner.ApiCredentials = [new KeyAndValue { Key = "primary", Value = key }];
        await bitween.Partners.UpdateAsync(partnerId, partner);

        var urlName = $"invoices-{Guid.NewGuid():N}"[..20];
        var gatewayId = await bitween.ApiGateways.CreateAsync(new ApiGatewayCreate { Name = Api.Unique("Invoices"), UrlName = urlName });
        await bitween.ApiGateways.AttachPartnerAsync(gatewayId, new ApiGatewayPartnerCreate { PartnerId = partnerId, SubscriptionId = subscriptionId });
        var attached = Assert.Single((await bitween.ApiGateways.GetAsync(gatewayId))!.Partners);
        Assert.Equal((partnerId, subscriptionId), (attached.PartnerId, attached.SubscriptionId));

        using var gateway = new BitweenGateway(fixture.Client(), key);

        var accepted = await gateway.SendAsync(urlName, "{\"invoice\":\"INV-1\"}", idempotencyKey: "INV-1");
        Assert.False(accepted.Replayed);
        var again = await gateway.SendAsync(urlName, "{\"invoice\":\"INV-1\"}", idempotencyKey: "INV-1");
        Assert.True(again.Replayed);
        Assert.Equal(accepted.ExchangeId, again.ExchangeId);

        var answer = await gateway.SendAndWaitAsync(urlName, "{\"invoice\":\"INV-2\"}", waitSeconds: 30);
        Assert.Equal(GatewayOutcome.Delivered, answer.Outcome);
        Assert.NotNull(answer.ExchangeId);
        Assert.Null(answer.Body);
        // The service account follows what the partner sent, by the id the gateway named.
        var exchange = await bitween.Exchanges.GetAsync(answer.ExchangeId!);
        Assert.Equal(partnerId, exchange!.PartnerId);

        var unknown = await Assert.ThrowsAsync<BitweenClientException>(() => gateway.SendAsync("no-such-gateway", "{}"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        using var stranger = new BitweenGateway(fixture.Client(), "not-a-key");
        var refused = await Assert.ThrowsAsync<BitweenClientException>(() => stranger.SendAsync(urlName, "{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);

        await bitween.ApiGateways.DetachPartnerAsync(gatewayId, partnerId);
        Assert.Empty((await bitween.ApiGateways.GetAsync(gatewayId))!.Partners);
        await Assert.ThrowsAsync<BitweenClientException>(() => gateway.SendAsync(urlName, "{}"));
        await bitween.ApiGateways.DeleteAsync(gatewayId);
    }

    [Fact]
    public async Task Tells_a_delivered_response_from_a_failed_delivery()
    {
        using var bitween = await ClientAsync(Configure);
        using var partner = new SyncResultSignalTests.EchoPartner();
        var (_, urlName, _, partnerId, subscriptionId) = await Api.GatewayAsync(fixture);
        var key = await bitween.Partners.NewKeyAsync();
        var record = (await bitween.Partners.GetAsync(partnerId))!;
        record.ApiCredentials = [new KeyAndValue { Key = "primary", Value = key }];
        await bitween.Partners.UpdateAsync(partnerId, record);

        async Task DeliverTo(string url)
        {
            var subscription = (await bitween.Subscriptions.GetAsync(subscriptionId))!;
            subscription.HandlerId = "NativeHttpHandler";
            subscription.HandlerProperties =
            [
                new KeyAndValue { Key = "Url", Value = url },
                new KeyAndValue { Key = "Verb", Value = "post" },
                new KeyAndValue { Key = "ContentType", Value = "application/json" },
            ];
            await bitween.Subscriptions.UpdateAsync(subscriptionId, subscription);
        }

        using var gateway = new BitweenGateway(fixture.Client(), key);

        await DeliverTo(partner.Url);
        var delivered = await gateway.SendAndWaitAsync(urlName, "{\"invoice\":\"INV-3\"}", waitSeconds: 30);
        Assert.Equal(GatewayOutcome.Delivered, delivered.Outcome);
        Assert.Contains("INV-3", delivered.Body);
        Assert.NotNull(delivered.ExchangeId);

        // Nothing listens on port 9 here: the delivery fails, and isn't retried without a policy.
        await DeliverTo("http://127.0.0.1:9/invoices");
        var failed = await gateway.SendAndWaitAsync(urlName, "{\"invoice\":\"INV-4\"}", waitSeconds: 30);
        Assert.Equal(GatewayOutcome.Failed, failed.Outcome);
        Assert.Equal(HttpStatusCode.BadGateway, failed.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        var exchange = await bitween.Exchanges.GetAsync(failed.ExchangeId!);
        Assert.False(exchange!.Status);
    }

    [Fact]
    public async Task Refuses_what_the_accounts_roles_dont_allow()
    {
        using var bitween = await ClientAsync("partners.view");
        var refused = await Assert.ThrowsAsync<BitweenClientException>(() =>
            bitween.Partners.CreateAsync(new PartnerCreate { Name = Api.Unique("Customer") }));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
    }
}
