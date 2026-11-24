using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A partner's own mapping of a subscription: used for their exchanges and nobody else's, and never
/// a way to read global values the subscription's own mapping doesn't already send them.
/// </summary>
[Collection("Bitween")]
public class PartnerMappingTests(BitweenFixture fixture)
{
    private static string Unique(string label) => $"{label} {Guid.NewGuid():N}";

    private static string Rules(params (string Target, string From)[] fields) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            version = 1,
            sourceFormat = "json",
            targetFormat = "json",
            fields = fields.Select(f => f.From.StartsWith("global:")
                ? (object)new { target = new[] { f.Target }, from = new { kind = "global", setId = f.From.Split(':')[1], key = f.From.Split(':')[2] } }
                : new { target = new[] { f.Target }, from = new { kind = "path", path = f.From } }).ToArray()
        });

    private static Dictionary<string, string> Props(string rules) => new()
    {
        ["MappingRules"] = rules, ["SourceSample"] = "", ["TargetSample"] = ""
    };

    private record World(Partner Mine, Partner Other, Subscription Subscription, int Person);

    private async Task<World> SetUpAsync(BitweenDbContext db, string baseRules)
    {
        var doc = new Document(null, Unique("Mapping doc"), DocumentFormat.Json);
        var mine = new Partner(Unique("Mapping mine"));
        var other = new Partner(Unique("Mapping other"));
        db.AddRange(doc, mine, other);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Mapping sub"), doc.Id, SubscriptionType.Internal, mine.Id)
        {
            MapperId = PartnerMappingRules.MapperId
        };
        subscription.SetDictionaries(new Dictionary<string, string>(), Props(baseRules),
            new Dictionary<string, string>(), null, new Dictionary<string, string>());
        db.Add(subscription);

        var role = new Role(Unique("Mapping editors"), null,
            [Permissions.Portal.MappingsView, Permissions.Portal.MappingsEdit], forPartners: true);
        db.Add(role);
        await db.SaveChangesAsync();

        var person = new Account("Mapper", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, mine.Id);
        db.Add(person);
        await db.SaveChangesAsync();
        db.Add(new AccountRoleLink(person.Id, role.Id));
        await db.SaveChangesAsync();
        return new World(mine, other, subscription, person.Id);
    }

    private async Task<string> RulesOfNewExchange(Subscription subscription, Partner partner)
    {
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
        await using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var xchange = await service.CreateXchange(subscription, new XchangeFile("{}"), gatewayPartner: partner);
        return xchange.MapperProperties["MappingRules"];
    }

    private async Task SaveAs(int person, int subscriptionId, string rules)
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        await ActivatorUtilities.CreateInstance<Resources.Portal.SaveMapping>(scope.ServiceProvider)
            .Handle(new PortalSaveMapping { SubscriptionId = subscriptionId, MapperProperties = Props(rules) });
    }

    [Fact]
    public async Task Their_mapping_is_used_for_their_exchanges_only_until_they_reset_it()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        var theirs = Rules(("reference", "orderId"), ("customer", "buyer.name"));

        await SaveAs(world.Person, world.Subscription.Id, theirs);

        Assert.Equal(theirs, await RulesOfNewExchange(world.Subscription, world.Mine));
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Other));

        await using (var reset = fixture.CreateScope())
        {
            reset.As(world.Person);
            await ActivatorUtilities.CreateInstance<Resources.Portal.ResetMapping>(reset.ServiceProvider)
                .Handle(new PortalMappingRequest { SubscriptionId = world.Subscription.Id });
        }
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
    }

    [Fact]
    public async Task A_retry_with_fresh_settings_keeps_their_mapping()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        var theirs = Rules(("reference", "orderId"));
        await SaveAs(world.Person, world.Subscription.Id, theirs);

        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var original = await service.CreateXchange(world.Subscription, new XchangeFile("{}"), gatewayPartner: world.Mine);
        await db.SaveChangesAsync();
        await service.CreateXchange(world.Subscription, original, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        var retry = await db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.RetryFor == original.Id);
        Assert.Equal(theirs, retry.MapperProperties["MappingRules"]);
    }

    [Fact]
    public async Task Their_mapping_reads_no_global_value_ours_does_not_already_send_them()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId"), ("region", "global:company:region")));

        // What ours already sends them, they may keep.
        await SaveAs(world.Person, world.Subscription.Id, Rules(("region", "global:company:region")));

        var byRule = await Assert.ThrowsAsync<SWValidationException>(() =>
            SaveAs(world.Person, world.Subscription.Id, Rules(("key", "global:company:apiKey"))));
        Assert.Contains("GLOBAL_VALUES_NOT_ALLOWED", byRule.Message);

        // Nor written as a placeholder, which is filled in before the mapper runs.
        var byPlaceholder = Rules(("id", "orderId")).Replace("orderId", "{{globals.company.apiKey}}");
        await Assert.ThrowsAsync<SWValidationException>(() => SaveAs(world.Person, world.Subscription.Id, byPlaceholder));
    }

    [Fact]
    public async Task Only_where_they_are_connected_and_only_with_the_edit_permission()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        var elsewhere = new Subscription(Unique("Not theirs"), world.Subscription.DocumentId, SubscriptionType.Internal, world.Other.Id)
        {
            MapperId = PartnerMappingRules.MapperId
        };
        db.Add(elsewhere);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SWNotFoundException>(() => SaveAs(world.Person, elsewhere.Id, Rules(("id", "orderId"))));

        var viewerRole = new Role(Unique("Mapping viewers"), null, [Permissions.Portal.MappingsView], forPartners: true);
        db.Add(viewerRole);
        await db.SaveChangesAsync();
        var viewer = new Account("Viewer", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, world.Mine.Id);
        db.Add(viewer);
        await db.SaveChangesAsync();
        db.Add(new AccountRoleLink(viewer.Id, viewerRole.Id));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => SaveAs(viewer.Id, world.Subscription.Id, Rules(("id", "orderId"))));
    }

    [Fact]
    public async Task Our_team_sees_who_maps_it_their_own_way_and_can_put_them_back_on_ours()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        var theirs = Rules(("reference", "orderId"));
        await SaveAs(world.Person, world.Subscription.Id, theirs);

        await using (var read = fixture.CreateScope())
        {
            read.Superuser();
            var rows = (List<SubscriptionPartnerMapping>)await ActivatorUtilities
                .CreateInstance<Resources.Subscriptions.PartnerMappings>(read.ServiceProvider).Handle(world.Subscription.Id);
            var row = Assert.Single(rows);
            Assert.Equal(world.Mine.Name, row.PartnerName);
            Assert.Equal(theirs, row.MappingRules);
            Assert.Equal("Mapper", row.ChangedBy);
        }

        // Only someone who can edit the subscription removes it.
        var viewer = await scope.AsNewViewer($"pm-viewer-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.RemovePartnerMapping>(scope.ServiceProvider)
            .Handle(world.Subscription.Id, new RemovePartnerMappingModel { PartnerId = world.Mine.Id }));

        await using (var remove = fixture.CreateScope())
        {
            remove.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.Subscriptions.RemovePartnerMapping>(remove.ServiceProvider)
                .Handle(world.Subscription.Id, new RemovePartnerMappingModel { PartnerId = world.Mine.Id });
        }
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
    }
}
