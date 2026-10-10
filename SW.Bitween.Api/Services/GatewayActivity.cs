using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// What partners' calls to gateways did that an operator can't otherwise see: when each API key was
/// last used, and the calls turned away. "The partner says they get 401" had nothing to look at.
/// </summary>
/// <remarks>
/// Refusals are kept in memory, per node, since the node started: writing each to the database
/// would let anyone with a gateway's address make Bitween write as fast as they could call it. Key
/// use is written to the database, but at most every <see cref="KeyUseResolution"/> per key and
/// node, so a busy key costs one small write every few minutes.
/// </remarks>
public class GatewayActivity(IServiceScopeFactory scopes, ILogger<GatewayActivity> logger)
{
    public static readonly TimeSpan KeyUseResolution = TimeSpan.FromMinutes(5);
    const int Kept = 500;

    public DateTime Since { get; } = DateTime.UtcNow;

    public record Refusal(DateTime On, string Gateway, string Reason, int Status, string Address);

    readonly ConcurrentQueue<Refusal> recent = new();
    readonly ConcurrentDictionary<(string Gateway, string Reason), long> counts = new();
    readonly ConcurrentDictionary<(int PartnerId, string KeyName), DateTime> written = new();

    /// <summary>A call to <paramref name="gateway"/> (its url name) turned away, and why.</summary>
    public void Refused(string gateway, string reason, int status, string address)
    {
        gateway = (gateway ?? "").Trim('/').ToLowerInvariant();
        counts.AddOrUpdate((gateway, reason), 1, (_, n) => n + 1);
        recent.Enqueue(new Refusal(DateTime.UtcNow, gateway, reason, status, address));
        while (recent.Count > Kept && recent.TryDequeue(out _)) { }
    }

    public (IReadOnlyDictionary<string, long> Counts, IReadOnlyList<Refusal> Recent) RefusalsOf(string gateway, int take = 25)
    {
        gateway = (gateway ?? "").Trim('/').ToLowerInvariant();
        return (counts.Where(c => c.Key.Gateway == gateway).ToDictionary(c => c.Key.Reason, c => c.Value),
            recent.Where(r => r.Gateway == gateway).OrderByDescending(r => r.On).Take(take).ToList());
    }

    /// <summary>Records that a key got a call through, unless this node did so moments ago.</summary>
    public async Task KeyUsedAsync(int partnerId, string keyName)
    {
        if (string.IsNullOrEmpty(keyName)) return;
        var now = DateTime.UtcNow;
        var slot = (partnerId, keyName);
        if (written.TryGetValue(slot, out var last) && now - last < KeyUseResolution) return;
        written[slot] = now;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var updated = await db.Set<ApiKeyUse>()
                .Where(u => u.PartnerId == partnerId && u.KeyName == keyName)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastUsedOn, now));
            if (updated == 0)
            {
                db.Add(new ApiKeyUse(partnerId, keyName, now));
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            // Another node may have added the row first, or the database is busy: the call went
            // through either way, and the next one after the resolution tries again.
            written.TryRemove(slot, out _);
            logger.LogDebug(ex, "Couldn't record a use of partner {PartnerId}'s key {KeyName}.", partnerId, keyName);
        }
    }
}
