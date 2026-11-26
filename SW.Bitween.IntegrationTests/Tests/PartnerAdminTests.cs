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
/// A partner's admin manages their own company's people in the portal: only their company, only roles
/// that can't manage people, within what they hold, and never themselves or another of their admins.
/// </summary>
[Collection("Bitween")]
public class PartnerAdminTests(BitweenFixture fixture)
{
    private record World(int Mine, int Other, int Admin, int AdminRole, int ViewerRole, int EditorRole);

    private async Task<World> SetUpAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await BuiltInPartnerRoles.EnsureAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>());
        var presets = await db.Set<Role>().AsNoTracking().Where(r => r.PresetKey != null)
            .ToDictionaryAsync(r => r.PresetKey, r => r.Id);

        var mine = new Partner($"Admin mine {Guid.NewGuid():N}");
        var other = new Partner($"Admin other {Guid.NewGuid():N}");
        db.AddRange(mine, other);
        await db.SaveChangesAsync();
        var admin = await PersonAsync(db, mine.Id, presets["partner-admin"]);
        return new World(mine.Id, other.Id, admin, presets["partner-admin"], presets["partner-viewer"], presets["partner-editor"]);
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

    private async Task<T> As<T>(int person, Func<IServiceProvider, Task<object>> act)
    {
        await using var scope = fixture.CreateScope();
        scope.As(person);
        return (T)await act(scope.ServiceProvider);
    }

    private Task<object> Run<THandler>(int person, Func<THandler, Task<object>> act) where THandler : class =>
        As<object>(person, sp => act(ActivatorUtilities.CreateInstance<THandler>(sp)));

    [Fact]
    public async Task Adds_someone_at_their_company_with_an_invite_and_a_role_they_may_give()
    {
        var world = await SetUpAsync();
        var email = $"{Guid.NewGuid():N}@acme.test";

        var added = (PortalPersonAdded)await Run<Resources.Portal.AddPerson>(world.Admin, h =>
            h.Handle(new PortalAddPerson { Name = "New person", Email = email, RoleId = world.EditorRole }));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = await db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == added.Id);
        Assert.Equal(world.Mine, account.PartnerId);
        Assert.True(string.IsNullOrEmpty(account.Password));
        Assert.True(added.Emailed || added.Link is not null);
        Assert.Equal([world.EditorRole],
            await db.Set<AccountRoleLink>().Where(l => l.AccountId == added.Id).Select(l => l.RoleId).ToListAsync());
    }

    [Fact]
    public async Task Can_not_give_a_role_that_manages_people_or_one_beyond_their_own()
    {
        var world = await SetUpAsync();
        await Assert.ThrowsAsync<SWValidationException>(() => Run<Resources.Portal.AddPerson>(world.Admin, h =>
            h.Handle(new PortalAddPerson { Name = "Admin two", Email = $"{Guid.NewGuid():N}@acme.test", RoleId = world.AdminRole })));
        await Assert.ThrowsAsync<SWValidationException>(() => Run<Resources.Portal.AddPerson>(world.Admin, h =>
            h.Handle(new PortalAddPerson { Name = "Staff", Email = $"{Guid.NewGuid():N}@acme.test", RoleId = Role.AdministratorId })));

        var choices = (List<PortalRoleChoice>)await Run<Resources.Portal.PeopleRoles>(world.Admin, h => h.Handle());
        // The least access first, so a new person starts there.
        Assert.Equal(world.ViewerRole, choices[0].Id);
        Assert.Contains(choices, c => c.Id == world.EditorRole);
        Assert.DoesNotContain(choices, c => c.Id == world.AdminRole);
    }

    [Fact]
    public async Task Changes_their_people_but_not_themselves_another_admin_or_another_company()
    {
        var world = await SetUpAsync();
        int viewer, otherAdmin, elsewhere;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            viewer = await PersonAsync(db, world.Mine, world.ViewerRole);
            otherAdmin = await PersonAsync(db, world.Mine, world.AdminRole);
            elsewhere = await PersonAsync(db, world.Other, world.ViewerRole);
        }

        await Run<Resources.Portal.SetPersonRole>(world.Admin, h =>
            h.Handle(new PortalPersonRole { AccountId = viewer, RoleId = world.EditorRole }));
        await Run<Resources.Portal.SetPersonDisabled>(world.Admin, h =>
            h.Handle(new PortalPersonDisabled { AccountId = viewer, Disabled = true }));

        await Assert.ThrowsAsync<SWValidationException>(() => Run<Resources.Portal.SetPersonDisabled>(world.Admin, h =>
            h.Handle(new PortalPersonDisabled { AccountId = world.Admin, Disabled = true })));
        await Assert.ThrowsAsync<SWValidationException>(() => Run<Resources.Portal.RemovePerson>(world.Admin, h =>
            h.Handle(new PortalPersonRequest { AccountId = otherAdmin })));
        await Assert.ThrowsAsync<SWNotFoundException>(() => Run<Resources.Portal.RemovePerson>(world.Admin, h =>
            h.Handle(new PortalPersonRequest { AccountId = elsewhere })));

        var people = (List<PortalPerson>)await Run<Resources.Portal.People>(world.Admin, h => h.Handle());
        Assert.Equal(new[] { world.Admin, viewer, otherAdmin }.Order(), people.Select(p => p.Id).Order());
        Assert.True(people.Single(p => p.Id == viewer).CanChange);
        Assert.True(people.Single(p => p.Id == viewer).Disabled);
        Assert.False(people.Single(p => p.Id == otherAdmin).CanChange);
        Assert.False(people.Single(p => p.Id == world.Admin).CanChange);

        await Run<Resources.Portal.RemovePerson>(world.Admin, h => h.Handle(new PortalPersonRequest { AccountId = viewer }));
    }

    [Fact]
    public async Task An_editor_can_not_see_or_manage_people()
    {
        var world = await SetUpAsync();
        int editor;
        await using (var scope = fixture.CreateScope())
            editor = await PersonAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), world.Mine, world.EditorRole);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() => Run<Resources.Portal.People>(editor, h => h.Handle()));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => Run<Resources.Portal.AddPerson>(editor, h =>
            h.Handle(new PortalAddPerson { Name = "x", Email = $"{Guid.NewGuid():N}@acme.test", RoleId = world.ViewerRole })));
    }

    [Fact]
    public async Task A_narrower_admin_sees_the_list_and_only_people_within_their_reach_as_changeable()
    {
        var world = await SetUpAsync();
        int narrow, editor, viewer;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            // Manages people without the view key, and can't change mappings.
            var role = new Role($"Narrow admin {Guid.NewGuid():N}", null,
                [Permissions.Portal.PeopleEdit, Permissions.Portal.SubscriptionsView, Permissions.Portal.ExchangesView,
                 Permissions.Portal.ExchangeFilesView, Permissions.Portal.ExchangeErrorsView, Permissions.Portal.MappingsView,
                 Permissions.Portal.PropertiesView],
                forPartners: true);
            db.Add(role);
            await db.SaveChangesAsync();
            narrow = await PersonAsync(db, world.Mine, role.Id);
            editor = await PersonAsync(db, world.Mine, world.EditorRole);
            viewer = await PersonAsync(db, world.Mine, world.ViewerRole);
        }

        var people = (List<PortalPerson>)await Run<Resources.Portal.People>(narrow, h => h.Handle());
        // The editor can change mappings, which this admin can't, so they're out of reach.
        Assert.False(people.Single(p => p.Id == editor).CanChange);
        Assert.True(people.Single(p => p.Id == viewer).CanChange);
        await Assert.ThrowsAsync<SWValidationException>(() => Run<Resources.Portal.SetPersonDisabled>(narrow, h =>
            h.Handle(new PortalPersonDisabled { AccountId = editor, Disabled = true })));
    }
}
