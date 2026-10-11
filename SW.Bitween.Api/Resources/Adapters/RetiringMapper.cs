using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween.Resources.Adapters;

/// <summary>
/// The Scriban mapper the native mapper replaced, offered only while something still runs on it.
/// </summary>
/// <remarks>
/// <para>
/// This is how the old mapper retires. A subscription already using it is untouched - it keeps
/// running, and it keeps its own editor - but the mapper stops being offered as a choice once the
/// last subscription has moved across. The list of mappers then shrinks by itself as the migration
/// finishes, rather than when someone remembers to delete the adapter.
/// </para>
/// <para>
/// One-way on purpose: past that point there is no picking it again. Which is why the question of
/// what counts as "in use" is answered generously - an inactive subscription still holds its
/// template and can be switched back on, so it counts. Withholding a mapper a subscription is
/// still saved with would leave that subscription's own mapper missing from the list its picker
/// draws from, which reads as no mapper chosen at all.
/// </para>
/// <para>
/// The same idea already exists a level down: <see cref="NativeAdapterDiscoveryService"/> withholds
/// the Rebex-backed adapters while no license key is set, so that a picker never offers something
/// that could only fail. This withholds one that would only lengthen a migration.
/// </para>
/// </remarks>
internal static class RetiringMapper
{
    /// <summary>The adapter id, which is the class name the discovery service reports.</summary>
    public const string Id = nameof(NativeJSONMapper);

    private static readonly string Lowered = Id.ToLowerInvariant();

    /// <summary>
    /// <paramref name="adapters"/> without the retiring mapper, unless a subscription still names it.
    /// </summary>
    /// <remarks>
    /// The database is only asked when the retiring mapper is in the list to begin with, so the
    /// other adapter kinds - receivers, handlers, validators - cost nothing.
    /// </remarks>
    public static async Task<List<string>> ExceptRetiring(
        this IEnumerable<string> adapters, BitweenDbContext dbContext)
    {
        var listed = adapters.ToList();

        if (!listed.Any(Matches)) return listed;

        // Compared lowered rather than as stored: the lookup that resolves a mapper at run time is
        // itself case-insensitive, so a row whose casing drifted is still a subscription running on
        // this mapper, and answering "no" to that would take the mapper away from it.
        var stillInUse = await dbContext.Set<Subscription>()
            .AnyAsync(s => s.MapperId != null && s.MapperId.ToLower() == Lowered);

        if (stillInUse) return listed;

        listed.RemoveAll(Matches);
        return listed;
    }

    private static bool Matches(string adapter) =>
        adapter.Equals(Id, StringComparison.OrdinalIgnoreCase);
}
