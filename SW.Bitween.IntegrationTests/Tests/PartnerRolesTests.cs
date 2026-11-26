using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A partner's admin makes roles for their own company's people: seen and held by their company only,
/// never managing people, never beyond what the admin holds; ours they may give but not change.
/// </summary>
[Collection("Bitween")]
public class PartnerRolesTests(BitweenFixture fixture)
{
    private record World(int Mine, int Other, int Admin, int OtherAdmin, int ViewerRole, int EditorRole);

    private async Task<World> SetUpAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await BuiltInPartnerRoles.EnsureAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>());
        var presets = await db.Set<Role>().AsNoTracking().Where(r => r.PresetKey != null)
            .ToDictionaryAsync(r => r.PresetKey, r => r.Id);
        var mine = new Partner($"Roles mine {Guid.NewGuid():N}");
        var other = new Partner($"Roles other {Guid.NewGuid():N}");
        db.AddRange(mine, other);
        await db.SaveChangesAsync();
        return new World(mine.Id, other.Id,
            await PersonAsync(db, mine.Id, presets["partner-admin"]),
            await PersonAsync(db, other.Id, presets["partner-admin"]),
            presets["partner-viewer"], presets["partner-editor"]);
    }

    private static async Task<int> PersonAsync(BitweenDbContext db, int partnerId, int roleId)
    {
        var account = new Account("Person", $"{Guid.NewGuid():N}@test.local", "hash", AccountRole.Viewer, partnerId);
        db.Add(account);
        await db.SaveChangesAsync();
        db.Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<object> Run<THandler>(int person, Func<THandler, Task<object>> act) where THandler : class
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return await act(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private async Task<object> Staff<THandler>(Func<THandler, Task<object>> act) where THandler : class
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return await act(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private Task<object> Save(int person, string name, params string[] permissions) =>
        Run<Resources.Portal.PortalRoleSaveHandler>(person, h => h.Handle(new PortalRoleSave
            { Name = name, Permissions = permissions.ToList() }));

    private static readonly string[] Looks = [Permissions.Portal.ExchangesView, Permissions.Portal.SubscriptionsView];

    [Fact]
    public async Task Their_role_is_theirs_alone_and_another_company_may_use_the_same_name()
    {
        var w = await SetUpAsync();
        var name = $"Operations {Guid.NewGuid():N}";
        var mine = (int)await Save(w.Admin, name, Looks);
        var theirs = (int)await Save(w.OtherAdmin, name, Looks);
        Assert.NotEqual(mine, theirs);

        var listed = (List<RoleRow>)await Run<Resources.Portal.PortalRoleList>(w.Admin, h => h.Handle());
        Assert.Contains(listed, r => r.Id == mine && r.PartnerId == w.Mine);
        Assert.DoesNotContain(listed, r => r.Id == theirs);
        Assert.Contains(listed, r => r.Id == w.ViewerRole);
        await Assert.ThrowsAsync<SWNotFoundException>(() =>
            Run<Resources.Portal.PortalRoleGet>(w.Admin, h => h.Handle(new PortalRoleRequest { Id = theirs })));

        // They give it to their own people; it can't go to anyone else's.
        var choices = (List<PortalRoleChoice>)await Run<Resources.Portal.PeopleRoles>(w.Admin, h => h.Handle());
        Assert.Contains(choices, c => c.Id == mine);
        Assert.DoesNotContain(choices, c => c.Id == theirs);
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var elsewhere = await PersonAsync(db, w.Other, w.ViewerRole);
        var refused = await Assert.ThrowsAsync<SWValidationException>(() =>
            Resources.Accounts.AccountRoles.Set(db, elsewhere, [mine]));
        Assert.Contains(refused.Validations, v => v.Key == "ROLE_DOESNT_SUIT_ACCOUNT");
    }

    [Fact]
    public async Task Their_role_never_manages_people_nor_goes_beyond_what_they_hold()
    {
        var w = await SetUpAsync();
        var people = await Assert.ThrowsAsync<SWValidationException>(() =>
            Save(w.Admin, $"Admins {Guid.NewGuid():N}", Permissions.Portal.PeopleView, Permissions.Portal.PeopleEdit));
        Assert.Contains(people.Validations, v => v.Key == "ROLE_MANAGES_PEOPLE");
        await Assert.ThrowsAsync<SWValidationException>(() =>
            Save(w.Admin, $"Staff {Guid.NewGuid():N}", Permissions.Partners.View));

        // An admin of theirs holding less than everything gives only what they hold.
        int narrowAdmin;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var role = Role.OwnedBy(w.Mine, $"Narrow {Guid.NewGuid():N}", null, []);
            db.Add(role);
            await db.SaveChangesAsync();
            // Only our team can make one of their admins, so this one is made directly.
            await db.Set<Role>().Where(r => r.Id == role.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Permissions,
                new List<string> { Permissions.Portal.PeopleView, Permissions.Portal.PeopleEdit, Permissions.Portal.ExchangesView }));
            narrowAdmin = await PersonAsync(db, w.Mine, role.Id);
        }
        var beyond = await Assert.ThrowsAsync<SWValidationException>(() =>
            Save(narrowAdmin, $"More {Guid.NewGuid():N}", Permissions.Portal.ExchangesView, Permissions.Portal.MappingsView));
        Assert.Contains(beyond.Validations, v => v.Key == "BEYOND_YOUR_PERMISSIONS");
        Assert.IsType<int>(await Save(narrowAdmin, $"Less {Guid.NewGuid():N}", Permissions.Portal.ExchangesView));
    }

    [Fact]
    public async Task They_change_and_delete_only_their_own()
    {
        var w = await SetUpAsync();
        var mine = (int)await Save(w.Admin, $"Mine {Guid.NewGuid():N}", Looks);
        var theirs = (int)await Save(w.OtherAdmin, $"Theirs {Guid.NewGuid():N}", Looks);

        var renamed = $"Renamed {Guid.NewGuid():N}";
        await Run<Resources.Portal.PortalRoleSaveHandler>(w.Admin, h => h.Handle(new PortalRoleSave
            { Id = mine, Name = renamed, Permissions = [Permissions.Portal.ExchangesView] }));
        var row = (RoleRow)await Run<Resources.Portal.PortalRoleGet>(w.Admin, h => h.Handle(new PortalRoleRequest { Id = mine }));
        Assert.Equal(renamed, row.Name);
        Assert.Equal([Permissions.Portal.ExchangesView], row.Permissions);

        var ours = await Assert.ThrowsAsync<SWValidationException>(() =>
            Run<Resources.Portal.PortalRoleSaveHandler>(w.Admin, h => h.Handle(new PortalRoleSave
                { Id = w.ViewerRole, Name = "Mine now", Permissions = Looks.ToList() })));
        Assert.Contains(ours.Validations, v => v.Key == "ROLE_IS_OURS");
        await Assert.ThrowsAsync<SWNotFoundException>(() =>
            Run<Resources.Portal.PortalRoleDelete>(w.Admin, h => h.Handle(new PortalRoleRequest { Id = theirs })));

        // Not while someone holds it.
        await using (var scope = fixture.CreateScope())
            await PersonAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), w.Mine, mine);
        var inUse = await Assert.ThrowsAsync<SWValidationException>(() =>
            Run<Resources.Portal.PortalRoleDelete>(w.Admin, h => h.Handle(new PortalRoleRequest { Id = mine })));
        Assert.Contains(inUse.Validations, v => v.Key == "ROLE_IN_USE");

        var spare = (int)await Save(w.Admin, $"Spare {Guid.NewGuid():N}", Looks);
        await Run<Resources.Portal.PortalRoleDelete>(w.Admin, h => h.Handle(new PortalRoleRequest { Id = spare }));
        await using var check = fixture.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Role>().AnyAsync(r => r.Id == spare));
    }

    [Fact]
    public async Task The_database_keeps_our_names_unique_and_lets_two_partners_share_one()
    {
        var w = await SetUpAsync();
        var name = $"Shared {Guid.NewGuid():N}";
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        db.AddRange(Role.OwnedBy(w.Mine, name, null, Looks), Role.OwnedBy(w.Other, name, null, Looks),
            new Role(name, null, Looks, forPartners: true));
        await db.SaveChangesAsync();

        // Past the checks in code, as two saves at once would be.
        await using var again = fixture.CreateScope();
        var db2 = again.ServiceProvider.GetRequiredService<BitweenDbContext>();
        db2.Add(new Role(name, null, Looks, forPartners: true));
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Our_team_sees_whose_a_role_is_and_it_goes_with_its_partner()
    {
        var w = await SetUpAsync();
        var name = $"Owned {Guid.NewGuid():N}";
        int owned;
        Partner gone;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            gone = new Partner($"Gone {Guid.NewGuid():N}");
            db.Add(gone);
            await db.SaveChangesAsync();
            var role = Role.OwnedBy(gone.Id, name, null, Looks);
            db.Add(role);
            await db.SaveChangesAsync();
            owned = role.Id;
        }

        var row = (RoleRow)await Staff<Resources.Roles.Get>( h => h.Handle(owned));
        Assert.Equal(gone.Id, row.PartnerId);
        Assert.Equal(gone.Name, row.PartnerName);

        // Our own role's name is clear of every partner's, so no partner ever sees two alike.
        var clash = await Assert.ThrowsAsync<SWValidationException>(() =>
            Staff<Resources.Roles.Create>( h => h.Handle(new RoleCreate
                { Name = name, ForPartners = true, Permissions = Looks.ToList() })));
        Assert.Contains(clash.Validations, v => v.Key == "ROLE_EXISTS");

        await Staff<Resources.Partners.Delete>( h => h.Handle(gone.Id));
        await using var check = fixture.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Role>().AnyAsync(r => r.Id == owned));
    }
}
