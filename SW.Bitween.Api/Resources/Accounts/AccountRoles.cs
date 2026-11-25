using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

internal static class AccountRoles
{
    /// <summary>Role summaries for a set of accounts, in one query - keyed by account id.</summary>
    public static async Task<Dictionary<int, List<AccountRoleSummary>>> For(BitweenDbContext dbContext,
        List<int> accountIds)
    {
        var links = await (from link in dbContext.Set<AccountRoleLink>()
                join role in dbContext.Set<Role>() on link.RoleId equals role.Id
                where accountIds.Contains(link.AccountId)
                select new { link.AccountId, role.Id, role.Name })
            .AsNoTracking()
            .ToListAsync();

        return links
            .GroupBy(l => l.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(l => l.Name)
                    .Select(l => new AccountRoleSummary { Id = l.Id, Name = l.Name })
                    .ToList());
    }

    public static async Task<List<AccountRoleSummary>> Of(BitweenDbContext dbContext, int accountId) =>
        (await For(dbContext, [accountId])).GetValueOrDefault(accountId, []);

    /// <summary>
    /// Replaces an account's roles. Rejects unknown ids so a stale UI can't silently strip access.
    /// </summary>
    public static async Task Set(BitweenDbContext dbContext, int accountId, List<int> roleIds)
    {
        var wanted = (roleIds ?? []).Distinct().ToList();
        EnsureOne(wanted);

        var known = await dbContext.Set<Role>().Where(r => wanted.Contains(r.Id)).Select(r => r.Id).ToListAsync();
        var unknown = wanted.Except(known).ToList();
        if (unknown.Count > 0)
            throw new SWValidationException("ROLE_NOT_FOUND",
                $"These roles don't exist: {string.Join(", ", unknown)}.");

        var isPartner = await dbContext.Set<Account>().AnyAsync(a => a.Id == accountId && a.PartnerId != null);
        await EnsureRolesSuit(dbContext, wanted, isPartner);

        var existing = await dbContext.Set<AccountRoleLink>().Where(l => l.AccountId == accountId).ToListAsync();

        foreach (var link in existing.Where(l => !wanted.Contains(l.RoleId)))
            dbContext.Remove(link);

        foreach (var roleId in wanted.Except(existing.Select(l => l.RoleId)))
            dbContext.Add(new AccountRoleLink(accountId, roleId));
    }

    /// <summary>
    /// Every account holds exactly one role. Roles used to add up, which nobody could read off the
    /// screen: Administrator ticked beside Member meant nothing more, and a custom role meant to
    /// add one thing to Viewer had to be remembered beside it. A custom role is now a whole set,
    /// made by duplicating the nearest built-in one. Accounts given several before keep them, and
    /// they still add up, until somebody changes their role.
    /// </summary>
    public static void EnsureOne(List<int> roleIds)
    {
        if ((roleIds ?? []).Distinct().Count() != 1)
            throw new SWValidationException("ONE_ROLE",
                "Choose one role. To give someone more than a role allows, duplicate it and add what they need.");
    }

    /// <summary>
    /// A partner's person holds partner roles only, and our own people never hold one: the two
    /// sides never share a role, so neither can be handed the other's access.
    /// </summary>
    public static async Task EnsureRolesSuit(BitweenDbContext dbContext, List<int> roleIds, bool isPartner)
    {
        var misfits = await dbContext.Set<Role>().AsNoTracking()
            .Where(r => roleIds.Contains(r.Id) && r.ForPartners != isPartner)
            .Select(r => r.Name)
            .ToListAsync();

        if (misfits.Count > 0)
            throw new SWValidationException("ROLE_DOESNT_SUIT_ACCOUNT", isPartner
                ? $"A partner's person can hold partner roles only. Not allowed: {string.Join(", ", misfits)}."
                : $"Partner roles are for a partner's people. Not allowed: {string.Join(", ", misfits)}.");
    }

    /// <summary>
    /// Keeps the legacy <see cref="Account.Role"/> column meaningful for older API consumers. It no
    /// longer drives authorization - it's derived from whichever built-in role the member holds,
    /// falling back to Member for someone who only holds custom roles.
    /// </summary>
    public static AccountRole LegacyRoleFor(List<int> roleIds)
    {
        if (roleIds.Contains(Role.AdministratorId)) return AccountRole.Admin;
        if (roleIds.Contains(Role.MemberId)) return AccountRole.Member;
        if (roleIds.Count == 0 || roleIds.Contains(Role.ViewerId)) return AccountRole.Viewer;
        return AccountRole.Member;
    }
}
