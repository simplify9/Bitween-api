using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
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

    private async Task<PortalMappingSaved> SaveAs(int person, int subscriptionId, string rules)
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return (PortalMappingSaved)await ActivatorUtilities.CreateInstance<Resources.Portal.SaveMapping>(scope.ServiceProvider)
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

    [Fact]
    public async Task Our_team_sees_their_own_mapping_on_the_partner_and_on_each_gateway()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));

        // Both partners reach the same subscription through each gateway; only one maps it their own way.
        var api = new ApiGateway
        {
            Name = Unique("Own gw"), UrlName = Guid.NewGuid().ToString("N")[..12],
            Partners =
            [
                new ApiGatewayPartner { PartnerId = world.Mine.Id, SubscriptionId = world.Subscription.Id },
                new ApiGatewayPartner { PartnerId = world.Other.Id, SubscriptionId = world.Subscription.Id }
            ]
        };
        var bus = new BusGateway { Name = Unique("Own bus"), DocumentId = world.Subscription.DocumentId };
        db.AddRange(api, bus);
        await db.SaveChangesAsync();
        db.AddRange(new BusGatewayRoute { BusGatewayId = bus.Id, SubscriptionId = world.Subscription.Id, PartnerId = world.Mine.Id },
            new BusGatewayRoute { BusGatewayId = bus.Id, SubscriptionId = world.Subscription.Id, PartnerId = world.Other.Id });
        await db.SaveChangesAsync();

        await using var read = fixture.CreateScope();
        read.Superuser();
        var listed = (List<PartnerOwnMapping>)await ActivatorUtilities
            .CreateInstance<Resources.Partners.OwnMappings>(read.ServiceProvider).Handle(world.Mine.Id);
        var own = Assert.Single(listed);
        Assert.Equal(world.Subscription.Id, own.SubscriptionId);
        Assert.Equal("Mapper", own.ChangedBy);
        Assert.Empty((List<PartnerOwnMapping>)await ActivatorUtilities
            .CreateInstance<Resources.Partners.OwnMappings>(read.ServiceProvider).Handle(world.Other.Id));

        var gateway = (ApiGatewayRow)await ActivatorUtilities
            .CreateInstance<Resources.ApiGateways.Get>(read.ServiceProvider).Handle(api.Id);
        Assert.True(gateway.Partners.Single(p => p.PartnerId == world.Mine.Id).OwnMapping);
        Assert.False(gateway.Partners.Single(p => p.PartnerId == world.Other.Id).OwnMapping);
        var page = await ActivatorUtilities.CreateInstance<Resources.ApiGateways.SearchAttachments>(read.ServiceProvider)
            .Handle(new SearchApiGatewayAttachmentsModel { ApiGatewayId = api.Id });
        var rows = (List<ApiGatewayPartnerDto>)page.GetType().GetProperty("Result")!.GetValue(page)!;
        Assert.Equal([true, false], rows.OrderBy(r => r.PartnerId != world.Mine.Id).Select(r => r.OwnMapping));

        var routes = ((BusGatewayRow)await ActivatorUtilities
            .CreateInstance<Resources.BusGateways.Get>(read.ServiceProvider).Handle(bus.Id)).Routes;
        Assert.True(routes.Single(r => r.PartnerId == world.Mine.Id).OwnMapping);
        Assert.False(routes.Single(r => r.PartnerId == world.Other.Id).OwnMapping);
    }

    [Fact]
    public async Task Only_the_rules_are_kept_never_the_samples()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));

        await using (var save = fixture.CreateScope())
        {
            save.As(world.Person);
            await ActivatorUtilities.CreateInstance<Resources.Portal.SaveMapping>(save.ServiceProvider).Handle(new PortalSaveMapping
            {
                SubscriptionId = world.Subscription.Id,
                MapperProperties = new()
                {
                    ["MappingRules"] = Rules(("reference", "orderId")),
                    ["SourceSample"] = "{\"customer\":\"a real one\"}",
                    ["TargetSample"] = "{}"
                }
            });
        }

        var stored = await db.Set<PartnerMapping>().AsNoTracking()
            .SingleAsync(m => m.SubscriptionId == world.Subscription.Id && m.PartnerId == world.Mine.Id);
        Assert.Equal(["MappingRules"], stored.MapperProperties.Keys.ToList());
    }

    [Fact]
    public async Task A_placeholder_in_our_sample_grants_nothing()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == world.Subscription.Id);
        subscription.SetDictionaries(new Dictionary<string, string>(), new Dictionary<string, string>
        {
            ["MappingRules"] = Rules(("id", "orderId")),
            ["SourceSample"] = "{{globals.company.apiKey}}"
        }, new Dictionary<string, string>(), null, new Dictionary<string, string>());
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SWValidationException>(() =>
            SaveAs(world.Person, world.Subscription.Id, Rules(("key", "global:company:apiKey"))));
    }

    [Fact]
    public async Task Theirs_is_set_aside_once_ours_stops_sending_a_global_value_it_reads()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var withRegion = Rules(("id", "orderId"), ("region", "global:company:region"));
        var world = await SetUpAsync(db, withRegion);
        var theirs = Rules(("where", "global:company:region"));
        await SaveAs(world.Person, world.Subscription.Id, theirs);
        Assert.Equal(theirs, await RulesOfNewExchange(world.Subscription, world.Mine));

        // Ours stops reading it.
        var ours = Rules(("id", "orderId"));
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == world.Subscription.Id);
        subscription.SetDictionaries(new Dictionary<string, string>(), Props(ours),
            new Dictionary<string, string>(), null, new Dictionary<string, string>());
        await db.SaveChangesAsync();

        Assert.Equal(ours, await RulesOfNewExchange(subscription, world.Mine));

        await using var read = fixture.CreateScope();
        read.As(world.Person);
        var places = (IEnumerable<PortalMappingPlace>)await ActivatorUtilities
            .CreateInstance<Resources.Portal.Mappings>(read.ServiceProvider).Handle();
        Assert.True(places.Single(p => p.SubscriptionId == world.Subscription.Id).SetAside);
    }

    private async Task NeedApproval(Partner partner, bool needed = true)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.Partners.SetApproval>(scope.ServiceProvider)
            .Handle(partner.Id, new PartnerApprovalUpdate { ChangesNeedApproval = needed });
    }

    private async Task<PartnerMappingChange> WaitingOf(World world)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<PartnerMappingChange>()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.SubscriptionId == world.Subscription.Id && c.PartnerId == world.Mine.Id &&
                                       c.Status == PartnerChangeStatus.Waiting);
    }

    private async Task Decide<THandler>(int changeId, string note = null) where THandler : class
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await ((dynamic)handler).Handle(changeId, new PartnerChangeDecision { Note = note });
    }

    private async Task<PortalMapping> ReadAs(World world)
    {
        await using var scope = fixture.CreateScope();
        scope.As(world.Person);
        return (PortalMapping)await ActivatorUtilities.CreateInstance<Resources.Portal.Mapping>(scope.ServiceProvider)
            .Handle(new PortalMappingRequest { SubscriptionId = world.Subscription.Id });
    }

    [Fact]
    public async Task With_approval_on_their_change_waits_until_approved()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        await NeedApproval(world.Mine);
        var first = Rules(("reference", "orderId"));
        var second = Rules(("reference", "orderId"), ("customer", "buyer.name"));

        await SaveAs(world.Person, world.Subscription.Id, first);
        await SaveAs(world.Person, world.Subscription.Id, second);

        // One change to decide on, the newest ask, and nothing used from it yet.
        var waiting = await WaitingOf(world);
        Assert.Equal(second, waiting.MapperProperties["MappingRules"]);
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
        Assert.NotNull((await ReadAs(world)).Waiting);

        // Someone who may only look can't approve.
        await using (var viewer = fixture.CreateScope())
        {
            await viewer.AsNewViewer($"pc-viewer-{Guid.NewGuid():N}");
            await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities
                .CreateInstance<Resources.PartnerChanges.Approve>(viewer.ServiceProvider)
                .Handle(waiting.Id, new PartnerChangeDecision()));
        }

        await Decide<Resources.PartnerChanges.Approve>(waiting.Id);
        Assert.Equal(second, await RulesOfNewExchange(world.Subscription, world.Mine));
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Other));
        Assert.Null((await ReadAs(world)).Waiting);
    }

    [Fact]
    public async Task A_rejected_change_leaves_what_they_have_and_says_why()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        await NeedApproval(world.Mine);

        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));
        await Decide<Resources.PartnerChanges.Reject>((await WaitingOf(world)).Id, "Keep the id field, please.");

        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
        var mapping = await ReadAs(world);
        Assert.Null(mapping.Waiting);
        Assert.Equal("Keep the id field, please.", mapping.Rejected?.Note);
    }

    [Fact]
    public async Task Going_back_to_ours_waits_for_approval_too()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        var theirs = Rules(("reference", "orderId"));
        await SaveAs(world.Person, world.Subscription.Id, theirs);
        await NeedApproval(world.Mine);

        await using (var reset = fixture.CreateScope())
        {
            reset.As(world.Person);
            await ActivatorUtilities.CreateInstance<Resources.Portal.ResetMapping>(reset.ServiceProvider)
                .Handle(new PortalMappingRequest { SubscriptionId = world.Subscription.Id });
        }
        Assert.Equal(theirs, await RulesOfNewExchange(world.Subscription, world.Mine));
        var waiting = await WaitingOf(world);
        Assert.True(waiting.BackToOurs);

        await Decide<Resources.PartnerChanges.Approve>(waiting.Id);
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
    }

    [Fact]
    public async Task Approving_checks_again_against_ours_as_it_is_now()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId"), ("region", "global:company:region")));
        await NeedApproval(world.Mine);
        await SaveAs(world.Person, world.Subscription.Id, Rules(("where", "global:company:region")));

        // Ours stops sending the value while their change waits.
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == world.Subscription.Id);
        subscription.SetDictionaries(new Dictionary<string, string>(), Props(Rules(("id", "orderId"))),
            new Dictionary<string, string>(), null, new Dictionary<string, string>());
        await db.SaveChangesAsync();

        var waiting = await WaitingOf(world);
        await Assert.ThrowsAsync<SWValidationException>(() => Decide<Resources.PartnerChanges.Approve>(waiting.Id));
        Assert.NotNull(await WaitingOf(world));
    }

    [Fact]
    public async Task Our_team_can_put_an_earlier_version_back()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        var first = Rules(("reference", "orderId"));
        var second = Rules(("ref", "orderId"));
        await SaveAs(world.Person, world.Subscription.Id, first);
        await SaveAs(world.Person, world.Subscription.Id, second);
        Assert.Equal(second, await RulesOfNewExchange(world.Subscription, world.Mine));

        List<PartnerChangeVersion> history;
        await using (var read = fixture.CreateScope())
        {
            read.Superuser();
            history = (List<PartnerChangeVersion>)await ActivatorUtilities
                .CreateInstance<Resources.PartnerChanges.History>(read.ServiceProvider)
                .Handle(new PartnerChangeHistoryRequest { SubscriptionId = world.Subscription.Id, PartnerId = world.Mine.Id });
        }
        Assert.Equal([second, first], history.Select(v => v.MappingRules).ToList());
        Assert.All(history, v => Assert.Equal("live", v.Status));

        await Decide<Resources.PartnerChanges.PutBack>(history.Single(v => v.MappingRules == first).Id);
        Assert.Equal(first, await RulesOfNewExchange(world.Subscription, world.Mine));
    }

    [Fact]
    public async Task With_approval_off_a_save_is_used_at_once_and_ends_one_still_waiting()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        await NeedApproval(world.Mine);
        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));
        var waitingId = (await WaitingOf(world)).Id;

        await NeedApproval(world.Mine, false);

        var now = Rules(("ref", "orderId"));
        var saved = await SaveAs(world.Person, world.Subscription.Id, now);
        Assert.False(saved.Waiting);
        Assert.Equal(now, await RulesOfNewExchange(world.Subscription, world.Mine));
        Assert.Equal(PartnerChangeStatus.Withdrawn, (await db.Set<PartnerMappingChange>().AsNoTracking()
            .SingleAsync(c => c.Id == waitingId)).Status);
    }

    [Fact]
    public async Task Approving_applies_exactly_the_change_the_reviewer_opened()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        await NeedApproval(world.Mine);
        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));
        var opened = (await WaitingOf(world)).Id;

        // The partner saves again while the reviewer looks at the first.
        var newer = Rules(("ref", "orderId"));
        await SaveAs(world.Person, world.Subscription.Id, newer);

        await Assert.ThrowsAsync<SWValidationException>(() => Decide<Resources.PartnerChanges.Approve>(opened));
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
        Assert.Equal(newer, (await WaitingOf(world)).MapperProperties["MappingRules"]);
    }

    [Fact]
    public async Task A_change_once_decided_can_not_be_decided_again()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ours = Rules(("id", "orderId"));
        var world = await SetUpAsync(db, ours);
        await NeedApproval(world.Mine);
        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));
        var id = (await WaitingOf(world)).Id;

        await Decide<Resources.PartnerChanges.Reject>(id, "No.");
        await Assert.ThrowsAsync<SWValidationException>(() => Decide<Resources.PartnerChanges.Approve>(id));
        await Assert.ThrowsAsync<SWValidationException>(() => Decide<Resources.PartnerChanges.Reject>(id));
        Assert.Equal(ours, await RulesOfNewExchange(world.Subscription, world.Mine));
    }

    [Fact]
    public async Task A_partner_whose_mapping_was_removed_stays_listed_for_its_history()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var world = await SetUpAsync(db, Rules(("id", "orderId")));
        await SaveAs(world.Person, world.Subscription.Id, Rules(("reference", "orderId")));

        await using var staff = fixture.CreateScope();
        staff.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.Subscriptions.RemovePartnerMapping>(staff.ServiceProvider)
            .Handle(world.Subscription.Id, new RemovePartnerMappingModel { PartnerId = world.Mine.Id });
        var listed = (List<SubscriptionPartnerMapping>)await ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.PartnerMappings>(staff.ServiceProvider).Handle(world.Subscription.Id);

        var mine = Assert.Single(listed, m => m.PartnerId == world.Mine.Id);
        Assert.False(mine.InUse);
        Assert.Null(mine.WaitingChangeId);
    }
}
