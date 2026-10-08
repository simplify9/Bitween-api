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
/// Managing members and roles can be delegated through a custom role. Holding users.edit or
/// roles.edit must not be a way to Administrator: a caller grants only what they hold, and leaves
/// alone anyone who holds more than they do.
/// </summary>
[Collection("Bitween")]
public class GrantLimitTests(BitweenFixture fixture)
{
    private static readonly string[] HelpdeskPermissions =
        [Permissions.Users.View, Permissions.Users.Edit, Permissions.Users.Create];

    private static readonly string[] RoleEditorPermissions =
        [Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Edit];

    [Fact]
    public async Task A_delegated_user_manager_cannot_make_themselves_administrator()
    {
        await using var scope = fixture.CreateScope();
        var (helpdesk, _) = await SignInWithCustomRole(scope, "self-admin", HelpdeskPermissions);

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(helpdesk, new SetAccountRolesModel { RoleIds = [Role.AdministratorId] }));
    }

    [Fact]
    public async Task A_delegated_user_manager_cannot_set_an_administrators_password()
    {
        await using var scope = fixture.CreateScope();
        var admin = await CreateAccount(scope, Unique("admin-target"), Role.AdministratorId);
        await SignInWithCustomRole(scope, "reset-admin", HelpdeskPermissions);

        var setPassword = ActivatorUtilities.CreateInstance<Resources.Accounts.SetPassword>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            setPassword.Handle(admin, new SetAccountPasswordModel { Password = "a-new-password-1" }));
    }

    [Fact]
    public async Task A_delegated_user_manager_can_grant_what_they_hold()
    {
        await using var scope = fixture.CreateScope();
        var (_, roleId) = await SignInWithCustomRole(scope, "grant-own", HelpdeskPermissions);
        var colleague = await CreateAccount(scope, Unique("colleague"));

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        await setRoles.Handle(colleague, new SetAccountRolesModel { RoleIds = [roleId] });

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.True(db.Set<AccountRoleLink>().Any(l => l.AccountId == colleague && l.RoleId == roleId));
    }

    [Fact]
    public async Task A_role_editor_cannot_add_a_permission_they_do_not_hold()
    {
        await using var scope = fixture.CreateScope();
        var (_, roleId) = await SignInWithCustomRole(scope, "role-edit", RoleEditorPermissions);

        var update = ActivatorUtilities.CreateInstance<Resources.Roles.Update>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            update.Handle(roleId, new RoleUpdate
            {
                Name = Unique("escalated"),
                Permissions = [.. RoleEditorPermissions, Permissions.Users.Edit],
            }));
    }

    [Fact]
    public async Task A_role_editor_cannot_create_a_role_beyond_their_own()
    {
        await using var scope = fixture.CreateScope();
        await SignInWithCustomRole(scope, "role-create", RoleEditorPermissions);

        var create = ActivatorUtilities.CreateInstance<Resources.Roles.Create>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            create.Handle(new RoleCreate
            {
                Name = Unique("everything"),
                Permissions = PermissionCatalog.AllKeys.ToList(),
            }));
    }

    [Fact]
    public async Task An_administrator_is_not_limited()
    {
        await using var scope = fixture.CreateScope();
        var admin = await CreateAccount(scope, Unique("admin-caller"), Role.AdministratorId);
        var target = await CreateAccount(scope, Unique("admin-grantee"));
        scope.As(admin);

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        await setRoles.Handle(target, new SetAccountRolesModel { RoleIds = [Role.AdministratorId] });
    }

    private static async Task<(int AccountId, int RoleId)> SignInWithCustomRole(
        AsyncServiceScope scope, string label, string[] permissions)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var role = new Role(Unique(label), "Delegated for a test", permissions.ToList());
        db.Set<Role>().Add(role);
        await db.SaveChangesAsync();

        var account = await CreateAccount(scope, Unique(label), role.Id);
        scope.As(account);
        return (account, role.Id);
    }

    private static async Task<int> CreateAccount(AsyncServiceScope scope, string label, params int[] roleIds)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = new Account("Test User", $"{label}@test.local", "irrelevant-hash", AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        foreach (var roleId in roleIds)
            db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static string Unique(string prefix) => $"{prefix}-{System.Guid.NewGuid():N}"[..24];
}
