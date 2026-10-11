using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

internal static class ServiceAccountStore
{
    public static async Task<Account> Load(BitweenDbContext dbContext, int id)
    {
        var account = await dbContext.Set<Account>().FindAsync(id);
        if (account is not { IsService: true } || account.Deleted)
            throw new SWValidationException("SERVICE_ACCOUNT_NOT_FOUND", $"No service account exists with the id {id}");
        return account;
    }

    public static async Task EnsureNameFree(BitweenDbContext dbContext, string name, int? except = null)
    {
        if (await dbContext.Set<Account>().AnyAsync(a =>
                a.Kind == AccountKind.Service && a.DisplayName == name && (except == null || a.Id != except)))
            throw new SWValidationException("SERVICE_ACCOUNT_EXISTS", $"There's already a service account named '{name}'.");
    }

    public static string Status(ServiceToken token, DateTime nowUtc) =>
        token.RevokedOn is not null ? "revoked" : token.ExpiresOn <= nowUtc ? "expired" : "active";

    public static T Describe<T>(ServiceToken token, DateTime nowUtc) where T : ServiceTokenModel, new() => new()
    {
        Id = token.Id,
        Name = token.Name,
        Shown = token.Shown,
        CreatedOn = token.CreatedOn,
        CreatedBy = token.CreatedBy,
        ExpiresOn = token.ExpiresOn,
        RevokedOn = token.RevokedOn,
        LastUsedOn = token.LastUsedOn,
        LastUsedFrom = token.LastUsedFrom,
        Status = Status(token, nowUtc),
    };

    public static async Task<List<ServiceAccountModel>> Describe(BitweenDbContext dbContext, IQueryable<Account> accounts)
    {
        var now = DateTime.UtcNow;
        var list = await accounts.AsNoTracking().OrderBy(a => a.DisplayName).ToListAsync();
        var ids = list.Select(a => a.Id).ToList();
        var roles = await AccountRoles.For(dbContext, ids);
        var tokens = (await dbContext.Set<ServiceToken>().AsNoTracking()
                .Where(t => ids.Contains(t.AccountId)).ToListAsync())
            .ToLookup(t => t.AccountId);

        return list.Select(a => new ServiceAccountModel
        {
            Id = a.Id,
            Name = a.DisplayName,
            Disabled = a.Disabled,
            CreatedOn = a.CreatedOn,
            LastSignInOn = a.LastSignInOn,
            Roles = roles.GetValueOrDefault(a.Id, []),
            // Active first, newest first; ended ones stay listed, for when they were used last.
            Tokens = tokens[a.Id]
                .OrderBy(t => Status(t, now) == "active" ? 0 : 1).ThenByDescending(t => t.CreatedOn)
                .Select(t => Describe<ServiceTokenModel>(t, now)).ToList(),
        }).ToList();
    }
}
