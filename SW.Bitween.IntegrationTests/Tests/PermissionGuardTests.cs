using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The guards in front of the handlers, exercised against real accounts and roles rather than the
/// superuser claim every other test uses.
/// </summary>
/// <remarks>
/// Two properties are load-bearing and neither is obvious from reading a handler. First, the
/// built-in roles don't store their grants — <c>Role.SystemPermissions</c> derives them at runtime,
/// so a Viewer's exact reach is a computed thing that could quietly change. Second, permissions are
/// resolved from the database on every call instead of being carried in the token, which is the
/// only reason revoking a role can take effect before the token expires. That second one is a
/// deliberate design decision with a real cost (a query per guarded call), so it deserves a test
/// proving the cost buys something.
/// </remarks>
[Collection("Bitween")]
public class PermissionGuardTests
{
    private readonly BitweenFixture _fixture;

    public PermissionGuardTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Account> CreateAccount(BitweenDbContext db, string email, params int[] roleIds)
    {
        var account = new Account("Test User", email, "irrelevant-hash", AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        foreach (var roleId in roleIds)
            db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();

        return account;
    }

    [Fact]
    public async Task Viewer_may_read_but_not_write()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var viewer = await CreateAccount(db, "viewer-rw@test.local", Role.ViewerId);
        var ctx = scope.As(viewer.Id);

        // The read side is what a Viewer exists for.
        await ctx.EnsurePermission(db, Permissions.Partners.View);

        // Every write is refused, across areas rather than just one, so this fails if the
        // view-only derivation ever starts leaking a create/edit/delete key.
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Partners.Create));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Subscriptions.Edit));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Documents.Delete));
    }

    [Fact]
    public async Task Member_may_write_integrations_but_not_manage_the_team()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var member = await CreateAccount(db, "member-scope@test.local", Role.MemberId);
        var ctx = scope.As(member.Id);

        await ctx.EnsurePermission(db, Permissions.Subscriptions.Create);
        await ctx.EnsurePermission(db, Permissions.Partners.Edit);

        // Users, roles and settings are the administrator's, and this is the line that keeps a
        // Member from granting themselves anything else.
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Users.Create));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Roles.Edit));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Settings.Edit));
    }

    [Fact]
    public async Task Administrator_holds_the_whole_catalog()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var admin = await CreateAccount(db, "admin-all@test.local", Role.AdministratorId);
        var granted = await RequestContextExtensions.GetPermissionsOf(db, admin.Id);

        Assert.Equal(PermissionCatalog.AllKeys.ToHashSet(), granted);
    }

    [Fact]
    public async Task Revoking_a_role_takes_effect_without_a_new_token()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var account = await CreateAccount(db, "revoked@test.local", Role.MemberId);
        var ctx = scope.As(account.Id);

        await ctx.EnsurePermission(db, Permissions.Subscriptions.Create);

        // Same RequestContext, same claims — only the database changed. If grants were baked into
        // the token this would keep passing until it expired.
        var link = db.Set<AccountRoleLink>().Single(l => l.AccountId == account.Id && l.RoleId == Role.MemberId);
        db.Set<AccountRoleLink>().Remove(link);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Subscriptions.Create));
    }

    [Fact]
    public async Task Granting_a_role_also_takes_effect_immediately()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var account = await CreateAccount(db, "granted@test.local");
        var ctx = scope.As(account.Id);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Subscriptions.Create));

        db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, Role.MemberId));
        await db.SaveChangesAsync();

        await ctx.EnsurePermission(db, Permissions.Subscriptions.Create);
    }

    [Fact]
    public async Task An_account_with_no_roles_is_granted_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var account = await CreateAccount(db, "no-roles@test.local");
        var ctx = scope.As(account.Id);

        Assert.Empty(await ctx.GetPermissions(db));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Partners.View));
    }

    [Fact]
    public async Task A_token_with_no_account_behind_it_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Fails closed rather than falling through to an empty grant set: a token that carries no
        // identifiable account is a broken token, not an unprivileged one.
        var ctx = scope.AsAnonymous();

        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ctx.GetPermissions(db));
    }

    [Fact]
    public async Task A_custom_role_grants_exactly_what_it_stores()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Non-system roles take their grants from the column, unlike the built-in three.
        var role = new Role("Exchange watcher", "Sees exchanges, nothing else",
            [Permissions.Exchanges.View]);
        db.Set<Role>().Add(role);
        await db.SaveChangesAsync();

        var account = await CreateAccount(db, "custom-role@test.local", role.Id);
        var ctx = scope.As(account.Id);

        await ctx.EnsurePermission(db, Permissions.Exchanges.View);
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            ctx.EnsurePermission(db, Permissions.Exchanges.Operate));
    }
}
