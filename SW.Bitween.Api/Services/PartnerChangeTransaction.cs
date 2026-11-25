using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SW.Bitween.Services;

/// <summary>
/// A partner change's writes as one: the change, its decision and what it applies are committed
/// together or not at all. The cache is cleared again after the commit: a save clears it as it goes,
/// which inside a transaction is before the commit, and a read in between would keep the old values.
/// </summary>
public static class PartnerChangeTransaction
{
    public static async Task<T> RunAsync<T>(BitweenDbContext db, IInfolinkCache cache, Func<Task<T>> work)
    {
        T result;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            result = await work();
            await transaction.CommitAsync();
        }

        if (cache is not null)
        {
            cache.Revoke();
            await cache.BroadcastRevoke();
        }
        return result;
    }
}
