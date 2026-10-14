using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// Honours an Idempotency-Key sent with a partner's call: within the window, the same key from the
/// same caller gets the exchange the first call created rather than a new one.
/// </summary>
public static class IdempotencyGuard
{
    public const string Header = "Idempotency-Key";
    public const int MaxKeyLength = 200;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private static DateTime _nextPrune = DateTime.MinValue;

    /// <summary>The exchange an earlier call with this key created, while the key is still live.</summary>
    public static async Task<string> FindAsync(BitweenDbContext dbContext, string scope, string key)
    {
        var since = DateTime.UtcNow - Window;
        return await dbContext.Set<IdempotencyKey>().AsNoTracking()
            .Where(k => k.Scope == scope && k.Key == key && k.CreatedOn > since)
            .Select(k => k.XchangeId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Stages the key with the exchange about to be saved, so the two commit together.
    /// </summary>
    /// <remarks>
    /// Inserted on the chance that the key is free, which it nearly always is. A key past its window
    /// still holds its place in the unique index; that used to be cleared with a delete on every call
    /// sending a key, and is now cleared only when one actually collides (<see cref="SaveAsync"/>).
    /// </remarks>
    public static async Task StageAsync(BitweenDbContext dbContext, string scope, string key, string xchangeId)
    {
        // Old keys go now and then, rather than through a job of their own.
        if (DateTime.UtcNow >= _nextPrune)
        {
            _nextPrune = DateTime.UtcNow.AddHours(1);
            var expired = DateTime.UtcNow - Window - TimeSpan.FromHours(1);
            await dbContext.Set<IdempotencyKey>().Where(k => k.CreatedOn < expired).ExecuteDeleteAsync();
        }

        dbContext.Add(new IdempotencyKey(scope, key, xchangeId));
    }

    /// <summary>
    /// Saves an exchange staged with its key. Answers with the exchange the key now stands for: this
    /// one, or the one an earlier call with the same key made, which the caller replays.
    /// </summary>
    public static async Task<(string XchangeId, bool Replayed)> SaveAsync(BitweenDbContext dbContext, string scope,
        string key, string xchangeId)
    {
        try
        {
            await dbContext.SaveChangesAsync();
            return (xchangeId, false);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Another call with the same key committed first: its exchange is the answer.
            var earlier = await FindAsync(dbContext, scope, key);
            if (earlier is null)
            {
                // The key that collided is past its window: it gives way, and this call takes it. The
                // exchange is still staged, so the same save goes again.
                var since = DateTime.UtcNow - Window;
                await dbContext.Set<IdempotencyKey>()
                    .Where(k => k.Scope == scope && k.Key == key && k.CreatedOn <= since)
                    .ExecuteDeleteAsync();
                try
                {
                    await dbContext.SaveChangesAsync();
                    return (xchangeId, false);
                }
                catch (DbUpdateException again) when (IsDuplicateKey(again))
                {
                    earlier = await FindAsync(dbContext, scope, key);
                }
            }

            dbContext.ChangeTracker.Clear();
            return (earlier, true);
        }
    }

    /// <summary>True when a save failed because another call with the same key committed first.</summary>
    public static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.Entries.Any(e => e.Entity is IdempotencyKey);
}
