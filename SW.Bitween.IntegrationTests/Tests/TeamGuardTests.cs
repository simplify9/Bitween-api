using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The rules that keep the team manageable: someone must always hold Administrator, a role that
/// people still hold can't vanish from under them, and two roles can't be told apart by name alone
/// if they share one.
/// </summary>
[Collection("Bitween")]
public class TeamGuardTests(BitweenFixture fixture)
{
    private static string Unique(string label) => $"{label}-{Guid.NewGuid():N}";

    private static async Task<Account> CreateAccount(BitweenDbContext db, string label, params int[] roleIds)
    {
        var account = new Account("Test User", $"{Unique(label)}@test.local", "irrelevant-hash", AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        foreach (var roleId in roleIds)
            db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();

        return account;
    }

    /// <summary>
    /// The collection shares one database, and other tests leave administrators in it. So the
    /// "only administrator" state is made inside a transaction — everyone else disabled, which the
    /// guard doesn't count — and rolled back afterwards, so no other test ever sees it.
    /// </summary>
    [Fact]
    public async Task The_last_administrator_cannot_lose_the_role_be_disabled_or_be_removed()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var admin = await CreateAccount(db, "last-admin", Role.AdministratorId);
        await db.Set<Account>().Where(a => a.Id != admin.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Disabled, true));

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        var setDisabled = ActivatorUtilities.CreateInstance<Resources.Accounts.SetDisabled>(scope.ServiceProvider);
        var remove = ActivatorUtilities.CreateInstance<Resources.Accounts.RemoveAccountModel>(scope.ServiceProvider);

        var demoted = await Assert.ThrowsAsync<SWValidationException>(() =>
            setRoles.Handle(admin.Id, new SetAccountRolesModel { RoleIds = [Role.MemberId] }));
        var disabled = await Assert.ThrowsAsync<SWValidationException>(() =>
            setDisabled.Handle(admin.Id, new SetAccountDisabledModel { Disabled = true }));
        var removed = await Assert.ThrowsAsync<SWValidationException>(() =>
            remove.Handle(admin.Id, null));

        foreach (var refusal in new[] { demoted, disabled, removed })
            Assert.StartsWith("LAST_ADMINISTRATOR", refusal.Message);
    }

    [Fact]
    public async Task A_role_someone_still_holds_cannot_be_deleted()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var role = new Role(Unique("in-use"), "Held by one member", [Permissions.Exchanges.View]);
        db.Set<Role>().Add(role);
        await db.SaveChangesAsync();
        var holder = await CreateAccount(db, "role-holder", role.Id);

        var delete = ActivatorUtilities.CreateInstance<Resources.Roles.Delete>(scope.ServiceProvider);

        var refusal = await Assert.ThrowsAsync<SWValidationException>(() => delete.Handle(role.Id));
        Assert.StartsWith("ROLE_IN_USE", refusal.Message);
        Assert.Contains("still assigned to 1 member", refusal.Message);

        // Once nobody holds it, the same delete goes through.
        db.Set<AccountRoleLink>().RemoveRange(db.Set<AccountRoleLink>().Where(l => l.AccountId == holder.Id));
        await db.SaveChangesAsync();
        await delete.Handle(role.Id);

        Assert.False(await db.Set<Role>().AnyAsync(r => r.Id == role.Id));
    }

    [Fact]
    public async Task A_role_cannot_take_a_name_another_role_already_has()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();

        var create = ActivatorUtilities.CreateInstance<Resources.Roles.Create>(scope.ServiceProvider);

        var refusal = await Assert.ThrowsAsync<SWValidationException>(() => create.Handle(new RoleCreate
        {
            Name = "Administrator",
            Description = "Should be refused.",
            Permissions = [Permissions.Exchanges.View],
        }));
        Assert.StartsWith("ROLE_EXISTS", refusal.Message);
    }
}
