using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Accounts;

internal static class Members
{
    /// <summary>
    /// A person's account by id, or null. Service accounts aren't members: what can be done to them
    /// is under service accounts, behind its own permissions, so the member endpoints don't find them.
    /// </summary>
    public static async Task<Account> FindAsync(BitweenDbContext dbContext, int id)
    {
        var account = await dbContext.Set<Account>().FindAsync(id);
        return account is { IsService: true } ? null : account;
    }
}
