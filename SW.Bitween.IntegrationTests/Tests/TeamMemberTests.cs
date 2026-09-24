using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// What an administrator changes about a member from the member drawer is really kept. The drawer
/// re-reads the member list after every save rather than trusting what it sent; these hold that
/// there's something there to read.
/// </summary>
[Collection("Bitween")]
public class TeamMemberTests(BitweenFixture fixture)
{
    private static async Task<Account> CreateViewer(BitweenDbContext db, string label)
    {
        var account = new Account("Test User", $"{label}-{Guid.NewGuid():N}@test.local", "irrelevant-hash",
            AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, Role.ViewerId));
        await db.SaveChangesAsync();

        return account;
    }

    [Fact]
    public async Task Setting_a_members_roles_replaces_the_ones_they_held()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = await CreateViewer(db, "role-swap");

        var setRoles = ActivatorUtilities.CreateInstance<Resources.Accounts.SetRoles>(scope.ServiceProvider);
        await setRoles.Handle(account.Id, new SetAccountRolesModel { RoleIds = [Role.MemberId] });

        var held = await db.Set<AccountRoleLink>().AsNoTracking()
            .Where(l => l.AccountId == account.Id)
            .Select(l => l.RoleId)
            .ToListAsync();
        Assert.Equal([Role.MemberId], held);
    }

    [Fact]
    public async Task A_disabled_member_stays_disabled_until_re_enabled()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = await CreateViewer(db, "on-leave");

        var setDisabled = ActivatorUtilities.CreateInstance<Resources.Accounts.SetDisabled>(scope.ServiceProvider);
        Task<bool> Stored() => db.Set<Account>().AsNoTracking()
            .Where(a => a.Id == account.Id)
            .Select(a => a.Disabled)
            .SingleAsync();

        await setDisabled.Handle(account.Id, new SetAccountDisabledModel { Disabled = true });
        Assert.True(await Stored());

        await setDisabled.Handle(account.Id, new SetAccountDisabledModel { Disabled = false });
        Assert.False(await Stored());
    }
}
