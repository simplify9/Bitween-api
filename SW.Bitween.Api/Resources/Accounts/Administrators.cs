using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

internal static class Administrators
{
    /// <summary>
    /// How many other enabled accounts still hold the Administrator role. Guards every operation
    /// that could otherwise leave an instance with nobody able to manage members and roles.
    /// </summary>
    public static Task<int> OtherThan(BitweenDbContext dbContext, int accountId) =>
        (from link in dbContext.Set<AccountRoleLink>()
            join account in dbContext.Set<Account>() on link.AccountId equals account.Id
            where link.RoleId == Role.AdministratorId && link.AccountId != accountId && !account.Disabled
            select link.AccountId).CountAsync();

    public static Task<bool> Holds(BitweenDbContext dbContext, int accountId) =>
        dbContext.Set<AccountRoleLink>()
            .AnyAsync(l => l.AccountId == accountId && l.RoleId == Role.AdministratorId);

    /// <summary>
    /// Blocks an operation that would remove the last administrator. Only applies when the account
    /// actually holds the role — otherwise nothing is being taken away.
    /// </summary>
    public static async Task EnsureNotTheLast(BitweenDbContext dbContext, int accountId)
    {
        if (!await Holds(dbContext, accountId))
            return;

        if (await OtherThan(dbContext, accountId) == 0)
            throw new SWValidationException("LAST_ADMINISTRATOR",
                "This is the only member with the Administrator role. Give it to someone else first.");
    }
}
