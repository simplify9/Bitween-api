using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.RebexFtpReceiver;
using SW.Bitween.NativeAdapters.RebexFtpUploadHandler;
using SW.Bitween.NativeAdapters.RebexPop3Receiver;

namespace SW.Bitween.Resources.Adapters;

/// <summary>
/// Built-in adapters on their way out, offered only while something still runs on them: the
/// Scriban mapper the native mapper replaced, and the Rebex adapters, which moved to the
/// marketplace as <c>s9.rebex.ftp.receiver</c>, <c>s9.rebex.ftp.upload</c> and
/// <c>s9.rebex.pop3.receiver</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is how a built-in adapter retires. A subscription already using it is untouched - it keeps
/// running, and it keeps its own editor - but the adapter stops being offered as a choice once the
/// last subscription has moved across. The list then shrinks by itself as the migration finishes,
/// rather than when someone remembers to delete the adapter.
/// </para>
/// <para>
/// One-way on purpose: past that point there is no picking it again. Which is why the question of
/// what counts as "in use" is answered generously - an inactive subscription still holds its
/// settings and can be switched back on, so it counts, and so does a notifier or a retry alert
/// naming a handler. Withholding an adapter a subscription is still saved with would leave that
/// subscription's own adapter missing from the list its picker draws from, which reads as no
/// adapter chosen at all.
/// </para>
/// <para>
/// The same idea already exists a level down: <see cref="NativeAdapterDiscoveryService"/> withholds
/// the Rebex-backed adapters while no license key is set, so that a picker never offers something
/// that could only fail. This withholds ones that would only lengthen a migration.
/// </para>
/// </remarks>
internal static class RetiringAdapters
{
    /// <summary>The old mapper's id, which is the class name the discovery service reports.</summary>
    public const string Mapper = nameof(NativeJSONMapper);

    /// <summary>Each retiring adapter's id, and whether anything this database holds still names it.</summary>
    private static readonly Dictionary<string, Func<BitweenDbContext, string, Task<bool>>> Retiring =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Mapper] = (db, id) => db.Set<Subscription>().AnyAsync(s => s.MapperId != null && s.MapperId.ToLower() == id),
            [nameof(NativeRebexFtpReceiver)] = ReceiverInUse,
            [nameof(NativeRebexPop3Receiver)] = ReceiverInUse,
            [nameof(NativeRebexFtpUploadHandler)] = HandlerInUse,
        };

    /// <summary>
    /// <paramref name="adapters"/> without the retiring ones nothing still names.
    /// </summary>
    /// <remarks>
    /// The database is only asked about a retiring adapter that is in the list to begin with, so
    /// the kinds with none, validators, cost nothing.
    /// </remarks>
    public static async Task<List<string>> ExceptRetiring(
        this IEnumerable<string> adapters, BitweenDbContext dbContext)
    {
        var listed = adapters.ToList();

        foreach (var adapter in listed.Where(Retiring.ContainsKey).ToList())
        {
            // Compared lowered rather than as stored: the lookup that resolves an adapter at run
            // time is itself case-insensitive, so a row whose casing drifted is still a
            // subscription running on it, and answering "no" to that would take it away.
            if (!await Retiring[adapter](dbContext, adapter.ToLowerInvariant()))
                listed.Remove(adapter);
        }

        return listed;
    }

    private static Task<bool> ReceiverInUse(BitweenDbContext db, string id) =>
        db.Set<Subscription>().AnyAsync(s => s.ReceiverId != null && s.ReceiverId.ToLower() == id);

    private static async Task<bool> HandlerInUse(BitweenDbContext db, string id) =>
        await db.Set<Subscription>().AnyAsync(s => s.HandlerId != null && s.HandlerId.ToLower() == id)
        || await db.Set<Notifier>().AnyAsync(n => n.HandlerId != null && n.HandlerId.ToLower() == id)
        || await db.Set<RetryPolicy>().AnyAsync(p => p.AlertHandlerId != null && p.AlertHandlerId.ToLower() == id)
        || await db.Set<RetryAlertOverride>().AnyAsync(o => o.AlertHandlerId != null && o.AlertHandlerId.ToLower() == id);
}
