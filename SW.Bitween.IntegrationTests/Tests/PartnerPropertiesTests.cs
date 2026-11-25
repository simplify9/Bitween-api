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
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A partner's own properties in the portal: only those we show them, a secret one never in clear,
/// changed only where we let them, through approval when their changes need it; and their own mapping
/// reads only the ones they see, or ones our mapping already sends them.
/// </summary>
[Collection("Bitween")]
public class PartnerPropertiesTests(BitweenFixture fixture)
{
    private record World(int Partner, int Person, int SubscriptionId);

    private async Task<World> SetUpAsync(bool needsApproval = false, string baseRules = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner($"Props {Guid.NewGuid():N}")
        {
            AdapterProperties = new() { ["webhookUrl"] = "https://acme/in", ["apiKey"] = "s3cret", ["accountNo"] = "A-1", ["internalCode"] = "X9" },
            SecretProperties = ["apiKey"],
            PortalAccess = new() { ["webhookUrl"] = "editable", ["apiKey"] = "editable", ["accountNo"] = "visible" },
            ChangesNeedApproval = needsApproval
        };
        var doc = new Document(null, $"Props doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Props sub {Guid.NewGuid():N}", doc.Id, SubscriptionType.Internal, partner.Id)
        {
            MapperId = PartnerMappingRules.MapperId
        };
        subscription.SetDictionaries(new Dictionary<string, string>(),
            new Dictionary<string, string> { ["MappingRules"] = baseRules ?? Rules(("id", "path", "orderId")) },
            new Dictionary<string, string>(), null, new Dictionary<string, string>());
        db.Add(subscription);
        var role = new Role($"Props role {Guid.NewGuid():N}", null,
            [Permissions.Portal.PropertiesView, Permissions.Portal.PropertiesEdit, Permissions.Portal.MappingsView, Permissions.Portal.MappingsEdit],
            forPartners: true);
        db.Add(role);
        await db.SaveChangesAsync();
        var person = new Account("Props person", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, partner.Id);
        db.Add(person);
        await db.SaveChangesAsync();
        db.Add(new AccountRoleLink(person.Id, role.Id));
        await db.SaveChangesAsync();
        return new World(partner.Id, person.Id, subscription.Id);
    }

    private static string Rules(params (string Target, string Kind, string Source)[] fields) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            version = 1, sourceFormat = "json", targetFormat = "json",
            fields = fields.Select(f => f.Kind == "partner"
                ? (object)new { target = new[] { f.Target }, from = new { kind = "partner", key = f.Source } }
                : new { target = new[] { f.Target }, from = new { kind = "path", path = f.Source } }).ToArray()
        });

    private async Task<object> As<THandler>(int person, Func<THandler, Task<object>> act) where THandler : class
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return await act(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private async Task<Dictionary<string, string>> StoredOf(int partnerId)
    {
        await using var scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Partner>().AsNoTracking()
            .SingleAsync(p => p.Id == partnerId)).AdapterProperties;
    }

    private Task<object> Save(World w, Dictionary<string, string> values) =>
        As<Resources.Portal.SaveProperties>(w.Person, h => h.Handle(new PortalSaveProperties { Values = values }));

    [Fact]
    public async Task They_see_only_what_we_show_them_and_never_a_secret()
    {
        var w = await SetUpAsync();
        var read = (PortalProperties)await As<Resources.Portal.Properties>(w.Person, h => h.Handle());

        Assert.Equal(["accountNo", "apiKey", "webhookUrl"], read.Properties.Select(p => p.Key).ToList());
        var key = read.Properties.Single(p => p.Key == "apiKey");
        Assert.True(key.Secret);
        Assert.Null(key.Value);
        Assert.False(read.Properties.Single(p => p.Key == "accountNo").Editable);
        Assert.True(read.Properties.Single(p => p.Key == "webhookUrl").Editable);
    }

    [Fact]
    public async Task They_change_only_what_we_let_them_and_an_empty_secret_is_kept()
    {
        var w = await SetUpAsync();
        await Save(w, new() { ["webhookUrl"] = "https://acme/v2", ["apiKey"] = "" });
        var stored = await StoredOf(w.Partner);
        Assert.Equal("https://acme/v2", stored["webhookUrl"]);
        Assert.Equal("s3cret", stored["apiKey"]);

        await Save(w, new() { ["apiKey"] = "n3w" });
        Assert.Equal("n3w", (await StoredOf(w.Partner))["apiKey"]);

        await Assert.ThrowsAsync<SWValidationException>(() => Save(w, new() { ["accountNo"] = "A-2" }));
        await Assert.ThrowsAsync<SWValidationException>(() => Save(w, new() { ["internalCode"] = "Y" }));
    }

    [Fact]
    public async Task With_approval_on_a_change_waits_until_approved_or_rejected()
    {
        var w = await SetUpAsync(needsApproval: true);
        await Save(w, new() { ["webhookUrl"] = "https://acme/v2" });
        Assert.Equal("https://acme/in", (await StoredOf(w.Partner))["webhookUrl"]);

        int waitingId;
        await using (var scope = fixture.CreateScope())
            waitingId = (await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<PartnerPropertyChange>()
                .SingleAsync(c => c.PartnerId == w.Partner && c.Status == PartnerChangeStatus.Waiting)).Id;

        await using (var staff = fixture.CreateScope())
        {
            staff.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.PartnerPropertyChanges.Approve>(staff.ServiceProvider)
                .Handle(waitingId, new PartnerChangeDecision());
        }
        Assert.Equal("https://acme/v2", (await StoredOf(w.Partner))["webhookUrl"]);

        await Save(w, new() { ["webhookUrl"] = "https://acme/v3" });
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var id = (await db.Set<PartnerPropertyChange>().SingleAsync(c => c.PartnerId == w.Partner && c.Status == PartnerChangeStatus.Waiting)).Id;
            scope.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.PartnerPropertyChanges.Reject>(scope.ServiceProvider)
                .Handle(id, new PartnerChangeDecision { Note = "Use the v2 address." });
        }
        Assert.Equal("https://acme/v2", (await StoredOf(w.Partner))["webhookUrl"]);
        var read = (PortalProperties)await As<Resources.Portal.Properties>(w.Person, h => h.Handle());
        Assert.Equal("Use the v2 address.", read.Rejected?.Note);
    }

    [Fact]
    public async Task Their_mapping_reads_only_what_they_see_or_ours_sends_them()
    {
        var w = await SetUpAsync(baseRules: Rules(("id", "path", "orderId"), ("code", "partner", "internalCode")));
        Task<object> SaveMapping(string rules) => As<Resources.Portal.SaveMapping>(w.Person, h => h.Handle(
            new PortalSaveMapping { SubscriptionId = w.SubscriptionId, MapperProperties = new() { ["MappingRules"] = rules } }));

        // Visible, and hidden but already sent by ours: both fine.
        await SaveMapping(Rules(("account", "partner", "accountNo"), ("code", "partner", "internalCode")));

        // Hidden and not ours: refused, by rule and by placeholder.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = await db.Set<Partner>().SingleAsync(p => p.Id == w.Partner);
            partner.AdapterProperties = new(partner.AdapterProperties) { ["hiddenOne"] = "h" };
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<SWValidationException>(() => SaveMapping(Rules(("x", "partner", "hiddenOne"))));
        await Assert.ThrowsAsync<SWValidationException>(() => SaveMapping(
            Rules(("id", "path", "orderId")).Replace("orderId", "{{partner.hiddenOne}}")));
    }

    [Fact]
    public async Task A_client_that_sends_no_portal_access_leaves_it_as_it_is()
    {
        var w = await SetUpAsync();
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == w.Partner);
        await ActivatorUtilities.CreateInstance<Resources.Partners.Update>(scope.ServiceProvider).Handle(w.Partner, new PartnerUpdate
        {
            Name = partner.Name,
            AdapterProperties = partner.AdapterProperties,
            SecretProperties = partner.SecretProperties,
        });

        await using var read = fixture.CreateScope();
        var after = await read.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Partner>().AsNoTracking()
            .SingleAsync(p => p.Id == w.Partner);
        Assert.Equal("editable", after.PortalAccess["webhookUrl"]);
        Assert.Equal("visible", after.PortalAccess["accountNo"]);
    }

    [Fact]
    public async Task A_guess_at_a_hidden_or_secret_value_is_answered_the_same_right_or_wrong()
    {
        var w = await SetUpAsync(needsApproval: true);
        // Hidden: refused whether the guess matches or not.
        await Assert.ThrowsAsync<SWValidationException>(() => Save(w, new() { ["internalCode"] = "X9" }));
        await Assert.ThrowsAsync<SWValidationException>(() => Save(w, new() { ["internalCode"] = "nope" }));

        // Secret: a change waiting for approval whether it matches or not.
        Assert.True(((PortalMappingSaved)await Save(w, new() { ["apiKey"] = "s3cret" })).Waiting);
        Assert.True(((PortalMappingSaved)await Save(w, new() { ["apiKey"] = "wrong" })).Waiting);
    }

    [Fact]
    public async Task Saving_another_detail_keeps_a_waiting_secret_replacement()
    {
        var w = await SetUpAsync(needsApproval: true);
        await Save(w, new() { ["apiKey"] = "n3w" });
        await Save(w, new() { ["apiKey"] = "", ["webhookUrl"] = "https://acme/v2" });

        await using var scope = fixture.CreateScope();
        var waiting = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<PartnerPropertyChange>()
            .AsNoTracking().SingleAsync(c => c.PartnerId == w.Partner && c.Status == PartnerChangeStatus.Waiting);
        Assert.Equal("n3w", waiting.Values["apiKey"]);
        Assert.Equal("https://acme/v2", waiting.Values["webhookUrl"]);
    }

    [Fact]
    public async Task A_property_hidden_after_they_asked_leaves_their_waiting_change_view()
    {
        var w = await SetUpAsync(needsApproval: true);
        await Save(w, new() { ["webhookUrl"] = "https://acme/v2" });
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = await db.Set<Partner>().SingleAsync(p => p.Id == w.Partner);
            partner.PortalAccess = new() { ["accountNo"] = "visible" };
            await db.SaveChangesAsync();
        }

        var read = (PortalProperties)await As<Resources.Portal.Properties>(w.Person, h => h.Handle());
        Assert.NotNull(read.Waiting);
        Assert.Empty(read.Waiting!.Values);
    }

    [Fact]
    public async Task Property_names_differing_only_in_case_are_refused()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await Assert.ThrowsAsync<SWValidationException>(() => ActivatorUtilities
            .CreateInstance<Resources.Partners.Create>(scope.ServiceProvider)
            .Handle(new PartnerCreate
            {
                Name = $"Twins {Guid.NewGuid():N}",
                AdapterProperties = new() { ["webhookUrl"] = "a", ["WEBHOOKURL"] = "b" }
            }));
    }
}
