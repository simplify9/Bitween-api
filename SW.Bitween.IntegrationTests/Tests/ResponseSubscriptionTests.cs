using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A response subscription: a pipeline whose only entry point is another subscription handing it
/// what its delivery sent back.
/// </summary>
[Collection("Bitween")]
public class ResponseSubscriptionTests(BitweenFixture fixture)
{
    private const string Responder = nameof(Adapters.NativeTestResponder);

    [Fact]
    public async Task A_response_subscription_runs_on_the_response_as_the_partner_that_fed_it()
    {
        var chain = await Arrange("{\"ack\":\"A-1\"}");

        var result = await Deliver(chain);

        Assert.True(result.Success, result.Exception);
        Assert.NotNull(result.ResponseXchangeId);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var response = await db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == result.ResponseXchangeId);

        Assert.Equal(chain.ResponseSubscriptionId, response.SubscriptionId);
        // The response subscription has no partner of its own. It is shared by everything that
        // feeds it, so the only partner that means anything is the one the delivery was made for.
        Assert.Equal(chain.PartnerId, response.PartnerId);
        Assert.Equal("http://host/acme", response.HandlerProperties["Url"]);
        Assert.Equal("{\"ack\":\"A-1\"}",
            await scope.ServiceProvider.GetRequiredService<XchangeService>().GetFile(response.Id, XchangeFileType.Input));
    }

    [Fact]
    public async Task A_bad_response_is_handed_on_only_when_the_response_subscription_asks_for_it()
    {
        const string error = "{\"error\":\"no such order\"}";

        var skipping = await Arrange(error, bad: true);
        var skipped = await Deliver(skipping);

        // The delivery's own outcome is recorded either way; only the hand-off is in question.
        Assert.True(skipped.ResponseBad, skipped.Exception);
        Assert.Null(skipped.ResponseXchangeId);

        var accepting = await Arrange(error, bad: true, runOnBadResponses: true);
        var accepted = await Deliver(accepting);

        Assert.True(accepted.ResponseBad, accepted.Exception);
        Assert.NotNull(accepted.ResponseXchangeId);
    }

    [Fact]
    public async Task A_paused_response_subscription_holds_the_response_and_releases_it_as_the_same_partner()
    {
        var chain = await Arrange("{\"ack\":\"H-1\"}", paused: true);

        var result = await Deliver(chain);

        Assert.True(result.Success, result.Exception);
        Assert.Null(result.ResponseXchangeId);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var held = await db.Set<OnHoldXchange>().AsNoTracking()
                .SingleAsync(h => h.SubscriptionId == chain.ResponseSubscriptionId);
            Assert.Equal("{\"ack\":\"H-1\"}", held.Data);
            Assert.Equal(chain.PartnerId, held.PartnerId);

            var target = await db.Set<Subscription>().SingleAsync(s => s.Id == chain.ResponseSubscriptionId);
            target.UnPause();
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        }

        await using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process(new SubscriptionUnpausedEvent { Id = chain.ResponseSubscriptionId });
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var released = await db.Set<Xchange>().AsNoTracking()
                .SingleAsync(x => x.SubscriptionId == chain.ResponseSubscriptionId);
            var source = await db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == result.Id);

            // Held and released later is still the same delivery's response: losing the partner
            // on the way would fill the handler's {{partner.…}} from nobody.
            Assert.Equal(chain.PartnerId, released.PartnerId);
            Assert.Equal("http://host/acme", released.HandlerProperties["Url"]);
            Assert.Equal(source.CorrelationId, released.CorrelationId);
            Assert.Equal("{\"ack\":\"H-1\"}",
                await scope.ServiceProvider.GetRequiredService<XchangeService>().GetFile(released.Id, XchangeFileType.Input));
            Assert.False(await db.Set<OnHoldXchange>().AnyAsync(h => h.SubscriptionId == chain.ResponseSubscriptionId));
        }
    }

    [Fact]
    public async Task A_disabled_response_subscription_does_not_fail_the_delivery_that_fed_it()
    {
        var chain = await Arrange("{\"ack\":\"D-1\"}", inactive: true);

        var result = await Deliver(chain);

        // It used to throw here — the lookup returns null for a disabled subscription — which
        // recorded a delivery that had succeeded as failed, and its retry delivered it again.
        Assert.True(result.Success, result.Exception);
        Assert.Null(result.Exception);
        Assert.Null(result.ResponseXchangeId);
    }

    [Fact]
    public async Task A_response_can_only_be_handed_to_a_response_subscription()
    {
        var (docId, internalId, responseId) = await Targets();

        Assert.Contains(await CreateFailures(docId, internalId), f => f.PropertyName == "ResponseSubscriptionId");
        Assert.DoesNotContain(await CreateFailures(docId, responseId), f => f.PropertyName == "ResponseSubscriptionId");
    }

    [Fact]
    public async Task A_legacy_target_already_saved_is_kept_but_cannot_be_chosen_again()
    {
        var (docId, internalId, _) = await Targets();
        var (_, otherInternalId, _) = await Targets();

        int sourceId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var source = new Subscription(Unique("Legacy source"), docId, SubscriptionType.BusGateway)
                { ResponseSubscriptionId = internalId };
            db.Add(source);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        // Saving the subscription for any other reason must not make its existing target invalid.
        Assert.DoesNotContain(await UpdateFailures(sourceId, internalId), f => f.PropertyName == "ResponseSubscriptionId");
        Assert.Contains(await UpdateFailures(sourceId, otherInternalId), f => f.PropertyName == "ResponseSubscriptionId");
    }

    [Fact]
    public async Task Response_subscriptions_cannot_hand_on_to_each_other_in_a_loop()
    {
        var (docId, _, firstId) = await Targets();
        int secondId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var second = new Subscription(Unique("Second response"), docId, SubscriptionType.Response)
                { ResponseSubscriptionId = firstId };
            db.Add(second);
            await db.SaveChangesAsync();
            secondId = second.Id;
        }

        // first → second is fine on its own; it is second → first, already saved, that closes it.
        var failures = await UpdateFailures(firstId, secondId);
        Assert.Contains(failures, f => f.PropertyName == "ResponseSubscriptionId" && f.ErrorMessage.Contains("loop"));
    }

    [Fact]
    public async Task A_new_response_subscription_is_created_in_the_same_save_as_the_one_feeding_it()
    {
        var (docId, _, _) = await Targets();
        var sourceId = await BusGatewaySubscription(docId);
        var name = Unique("Inline response");

        await Update(sourceId, new SubscriptionUpdate
        {
            Name = "Feeds a new one",
            HandlerId = Responder,
            NewResponseSubscription = new InlineIntegrationCreate
            {
                Name = name, DocumentId = docId, HandlerId = Responder, RunOnBadResponses = true,
            },
        });

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var response = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Name == name);
        var source = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == sourceId);

        Assert.Equal(response.Id, source.ResponseSubscriptionId);
        Assert.Equal(SubscriptionType.Response, response.Type);
        Assert.False(response.Inactive);
        Assert.True(response.RunOnBadResponses);
    }

    [Fact]
    public async Task A_new_route_brings_its_subscription_and_that_ones_response_subscription_together()
    {
        var (docId, _, _) = await Targets();
        var responseName = Unique("Route response");

        int gatewayId, routeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            gatewayId = (int)await ActivatorUtilities.CreateInstance<Resources.BusGateways.Create>(scope.ServiceProvider)
                .Handle(new BusGatewayCreate { Name = Unique("Response gateway"), DocumentId = docId });
        }
        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            routeId = (int)await ActivatorUtilities.CreateInstance<Resources.BusGateways.AddRoute>(scope.ServiceProvider)
                .Handle(gatewayId, new BusGatewayRouteCreate
                {
                    NewIntegration = new InlineIntegrationCreate
                    {
                        Name = Unique("Route subscription"),
                        HandlerId = Responder,
                        NewResponseSubscription = new InlineIntegrationCreate
                            { Name = responseName, DocumentId = docId, HandlerId = Responder },
                    },
                });
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var route = await db.Set<BusGatewayRoute>().AsNoTracking().SingleAsync(r => r.Id == routeId);
            var runs = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == route.SubscriptionId);
            var response = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Name == responseName);
            Assert.Equal(response.Id, runs.ResponseSubscriptionId);
            Assert.Equal(SubscriptionType.Response, response.Type);
        }
    }

    [Fact]
    public async Task Naming_an_existing_and_a_new_response_subscription_at_once_is_refused()
    {
        var (docId, _, responseId) = await Targets();
        var sourceId = await BusGatewaySubscription(docId);
        var name = Unique("Never created");

        await Assert.ThrowsAsync<SWValidationException>(() => Update(sourceId, new SubscriptionUpdate
        {
            Name = "Both",
            HandlerId = Responder,
            ResponseSubscriptionId = responseId,
            NewResponseSubscription = new InlineIntegrationCreate { Name = name, DocumentId = docId, HandlerId = Responder },
        }));

        await using var scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Subscription>().AnyAsync(s => s.Name == name));
    }

    [Fact]
    public async Task Defining_a_new_subscription_inline_needs_the_create_permission()
    {
        var (docId, _, _) = await Targets();
        var sourceId = await BusGatewaySubscription(docId);
        var name = Unique("Needs create");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var role = new Role(Unique("edit-only"), "Edits, never creates",
            [Permissions.Subscriptions.Edit, Permissions.BusGateways.Edit, Permissions.ApiGateways.Edit]);
        db.Set<Role>().Add(role);
        var account = new Account("Editor", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();
        db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, role.Id));
        await db.SaveChangesAsync();
        scope.As(account.Id);

        var inline = new InlineIntegrationCreate { Name = name, DocumentId = docId, HandlerId = Responder };
        var update = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider);

        // Editing alone is still theirs; bringing a new subscription along with the edit is not.
        await update.Handle(sourceId, new SubscriptionUpdate { Name = "Edited", HandlerId = Responder });
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => update.Handle(sourceId,
            new SubscriptionUpdate { Name = "Edited", HandlerId = Responder, NewResponseSubscription = inline }));
        // The same for the gateways' inline create, refused before the gateway is even looked up.
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ActivatorUtilities.CreateInstance<Resources.BusGateways.AddRoute>(scope.ServiceProvider)
                .Handle(0, new BusGatewayRouteCreate { NewIntegration = inline }));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ActivatorUtilities.CreateInstance<Resources.ApiGateways.AddPartner>(scope.ServiceProvider)
                .Handle(0, new ApiGatewayPartnerCreate { NewIntegration = inline }));

        Assert.False(await db.Set<Subscription>().AnyAsync(s => s.Name == name));
    }

    // ---------------------------------------------------------------- arrangement

    private record Chain(int SourceSubscriptionId, int ResponseSubscriptionId, int PartnerId);

    /// <summary>
    /// A partner, a bus-gateway subscription whose delivery answers <paramref name="response"/>,
    /// and the response subscription it hands that answer to.
    /// </summary>
    private async Task<Chain> Arrange(string response, bool bad = false, bool runOnBadResponses = false,
        bool paused = false, bool inactive = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner(Unique("Response partner"))
        {
            AdapterProperties = new Dictionary<string, string> { ["slug"] = "acme" }
        };
        var doc = new Document(null, Unique("Response doc"), DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();

        var target = new Subscription(Unique("Response target"), doc.Id, SubscriptionType.Response)
        {
            Inactive = inactive,
            HandlerId = Responder,
            RunOnBadResponses = runOnBadResponses,
        };
        target.SetDictionaries(Props(("Url", "http://host/{{partner.slug}}")), Props(), Props(), Props(), Props());
        if (paused) target.Pause();
        db.Add(target);
        await db.SaveChangesAsync();

        var source = new Subscription(Unique("Response source"), doc.Id, SubscriptionType.BusGateway)
        {
            Inactive = false,
            HandlerId = Responder,
            ResponseSubscriptionId = target.Id,
        };
        source.SetDictionaries(Props(("Body", response), ("Bad", bad.ToString())), Props(), Props(), Props(), Props());
        db.Add(source);
        await db.SaveChangesAsync();

        return new Chain(source.Id, target.Id, partner.Id);
    }

    /// <summary>Runs one delivery of the source, made for the chain's partner, to its result.</summary>
    private async Task<XchangeResult> Deliver(Chain chain)
    {
        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            // Created after the cache warmed; the response target is looked up through it.
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

            var source = await db.Set<Subscription>().SingleAsync(s => s.Id == chain.SourceSubscriptionId);
            var partner = await db.Set<Partner>().SingleAsync(p => p.Id == chain.PartnerId);
            // As a bus route with a partner would: the partner comes from the entry point.
            var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .CreateXchange(source, new XchangeFile("{\"order\":\"1001\"}"), null, Guid.NewGuid().ToString("N"), partner);
            await db.SaveChangesAsync();
            xchangeId = xchange.Id;
        }

        await using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using (var scope = fixture.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        }
    }

    private async Task<int> BusGatewaySubscription(int docId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var source = new Subscription(Unique("Feeding source"), docId, SubscriptionType.BusGateway);
        db.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task Update(int subscriptionId, SubscriptionUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider)
            .Handle(subscriptionId, model);
    }

    /// <summary>A document with one legacy Internal subscription and one response subscription on it.</summary>
    private async Task<(int DocId, int InternalId, int ResponseId)> Targets()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Legacy partner"));
        var doc = new Document(null, Unique("Target doc"), DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();

        var legacy = new Subscription(Unique("Legacy internal"), doc.Id, SubscriptionType.Internal, partner.Id);
        var response = new Subscription(Unique("Response"), doc.Id, SubscriptionType.Response);
        db.AddRange(legacy, response);
        await db.SaveChangesAsync();
        return (doc.Id, legacy.Id, response.Id);
    }

    // The validators are private nested classes the request pipeline finds by scanning, so calling
    // a handler directly never runs them. Built by reflection, the way AccountCreateValidatorTests does.

    private async Task<IList<FluentValidation.Results.ValidationFailure>> CreateFailures(int docId, int responseSubscriptionId)
    {
        await using var scope = fixture.CreateScope();
        var validator = (IValidator<SubscriptionCreate>)Activator.CreateInstance(
            typeof(Resources.Subscriptions.Create).GetNestedType("Validate", BindingFlags.NonPublic)!,
            scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
            scope.ServiceProvider.GetRequiredService<AdapterRequirements>())!;

        var result = await validator.ValidateAsync(new SubscriptionCreate
        {
            Name = Unique("New source"),
            DocumentId = docId,
            Type = SubscriptionType.BusGateway,
            ResponseSubscriptionId = responseSubscriptionId,
        });
        return result.Errors;
    }

    private async Task<IList<FluentValidation.Results.ValidationFailure>> UpdateFailures(int subscriptionId, int responseSubscriptionId)
    {
        await using var scope = fixture.CreateScope();
        // The update validator learns which subscription it is saving from the request path.
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Request.Path = $"/api/subscriptions/{subscriptionId}";

        var validator = (IValidator<SubscriptionUpdate>)Activator.CreateInstance(
            typeof(Resources.Subscriptions.Update).GetNestedType("Validate", BindingFlags.NonPublic)!,
            scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), http,
            scope.ServiceProvider.GetRequiredService<AdapterRequirements>())!;

        var result = await validator.ValidateAsync(new SubscriptionUpdate
        {
            Name = "Saved again",
            ResponseSubscriptionId = responseSubscriptionId,
        });
        return result.Errors;
    }

    private static Dictionary<string, string> Props(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";
}
