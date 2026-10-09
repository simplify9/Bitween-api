using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

/// <summary>One adapter of a kind, and the versions of it that are published.</summary>
/// <param name="Key">The adapter id, as a subscription stores it.</param>
/// <param name="Native">In-process, so it has no published versions of its own.</param>
/// <param name="VersionPaths">
/// Published version files, as paths relative to the remote adapter root — the trailing segment is
/// the version number.
/// </param>
/// <param name="Manifest">
/// What the adapter says about itself — the current package's manifest, or a native adapter's. Null
/// for a package published before manifests existed.
/// </param>
/// <param name="Catalog">The published adapter's catalog entry; null when it has none.</param>
public record AdapterEntry(string Key, bool Native, IReadOnlyList<string> VersionPaths,
    SW.Serverless.Contract.Catalog.AdapterManifest Manifest = null,
    SW.Serverless.Contract.Catalog.AdapterCatalogEntry Catalog = null);

/// <summary>
/// The list of adapters of one kind: the in-process ones plus whatever is published to storage.
/// </summary>
/// <remarks>
/// Shared by <see cref="SearchVersioned"/>, which answers with the shape the older UI reads, and
/// <see cref="Catalog"/>, which answers with the same list plus each adapter's startup properties.
/// The grouping of version files under their adapter is fiddly enough that a second copy of it
/// would be a second thing to get wrong.
/// </remarks>
public class AdapterListing(
    ServerlessOptions serverlessOptions,
    ICloudFilesService cloudFilesService,
    NativeAdapterDiscoveryService nativeAdapterDiscovery,
    BitweenDbContext dbContext,
    SW.Serverless.AdapterInstaller adapterInstaller,
    Services.Adapters.AdapterCatalog catalog,
    Services.Adapters.NativeAdapterManifests nativeManifests)
{
    /// <param name="prefix">The plural, lowercase kind: <c>receivers</c>, <c>handlers</c>, …</param>
    /// <returns>Native adapters first, then the published ones.</returns>
    public async Task<List<AdapterEntry>> List(string prefix)
    {
        var index = serverlessOptions.AdapterRemotePath.Length + 1;

        var native = (await nativeAdapterDiscovery.GetNativeAdapters(prefix).ExceptRetiring(dbContext))
            .Select(key => new AdapterEntry(key, true, [], nativeManifests.Get(key)))
            .ToList();

        var files = (await ListByKindAsync(prefix))
            .Where(item => item.Size > 0)
            .ToList();

        var published = files
            .GroupBy(i =>
            {
                var lastSection = i.Key.Split("/").Last();
                var isSemver = Semver.IsVersionNumber(lastSection);
                return isSemver ? i.Key.Split("/").ElementAt(^2) : lastSection;
            })
            .Select(g => new AdapterEntry(
                g.Key,
                false,
                g.Where(v => v.Key != g.Key && Semver.IsVersionNumber(v.Key.Split("/").Last()))
                    .Select(v => v.Key[index..])
                    .ToList()));

        // The catalog adds what a package listing cannot: the current version, the history, and
        // the presentation. An adapter without an entry is listed exactly as before.
        var withCatalog = await Task.WhenAll(published.Select(async entry =>
        {
            var found = await catalog.GetAsync(entry.Key);
            return found == null ? entry : entry with { Manifest = found.Manifest, Catalog = found };
        }));

        var listed = withCatalog.Select(e => e.Key).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var catalogOnly = await CatalogOnlyAsync(prefix, listed);

        return native.Concat(withCatalog).Concat(catalogOnly).ToList();
    }

    /// <summary>
    /// Adapters that are in the catalog but have no package at <c>adapters/{id}</c>, which is all
    /// the listing above looks at. An adapter in another runtime than .NET is never put there —
    /// older hosts would start it with dotnet — and neither is a .NET version published without
    /// being made current. Each is listed under the kinds its manifests declare, or the old
    /// naming convention, and only while it has a version that can still be used.
    /// </summary>
    private async Task<List<AdapterEntry>> CatalogOnlyAsync(string prefix, ISet<string> listed)
    {
        var kind = prefix?.TrimEnd('s') ?? "";
        var root = serverlessOptions.AdapterRemotePath;
        var catalogRoot = SW.Serverless.Contract.Catalog.AdapterCatalogPaths.CatalogRoot(root);

        IEnumerable<CloudFileInfo> files;
        try
        {
            files = await cloudFilesService.ListAsync(catalogRoot);
        }
        catch
        {
            // As everywhere the catalog is read: absent rather than fatal.
            return [];
        }

        var ids = files
            .Where(f => f.Key.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Key[catalogRoot.Length..^".json".Length])
            .Where(id => id.Length > 0 && !id.Contains('/') && !listed.Contains(id))
            .ToList();

        var entries = await Task.WhenAll(ids.Select(id => catalog.GetAsync(id)));
        return entries
            .Where(e => e != null && e.Versions.Any(v => !v.Withdrawn))
            .Where(e => e.Id.StartsWith($"infolink6.{prefix}.", System.StringComparison.OrdinalIgnoreCase) ||
                        (kind.Length > 0 && e.Versions.Append(new() { Manifest = e.Manifest })
                            .Any(v => v.Manifest?.Kinds?.Contains(kind, System.StringComparer.OrdinalIgnoreCase) == true)))
            .Select(e => new AdapterEntry(e.Id, false, [], e.Manifest ?? e.Versions.Last(v => !v.Withdrawn).Manifest, e))
            .ToList();
    }

    /// <summary>
    /// Everything published under the old naming convention for this kind, plus everything that
    /// DECLARED the kind in its metadata whatever it is called.
    ///
    /// Two ways an adapter says what it is for, and both are honoured. The Kind stamped on it at
    /// publish time is the real answer: it comes from the code rather than from whoever typed the
    /// id, it is the only way a third party's adapter can be found at all, and it lets an adapter
    /// be reclassified without being renamed — a rename is not free, because every subscription
    /// stores the id.
    ///
    /// The infolink6.&lt;kind&gt;s. prefix is the old convention, and everything published before the
    /// stamp existed carries nothing else. Dropping it would empty this list on every deployment
    /// that has not republished, so it stays as the fallback.
    /// </summary>
    private async Task<List<CloudFileInfo>> ListByKindAsync(string prefix)
    {
        var root = serverlessOptions.AdapterRemotePath;

        var byConvention = (await cloudFilesService.ListAsync($"{root}/infolink6.{prefix}")).ToList();
        var named = byConvention.Select(i => i.Key).ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        // The plural the UI asks with — "handlers" — against the singular an adapter declares.
        var kind = prefix?.TrimEnd('s') ?? "";
        if (string.IsNullOrWhiteSpace(kind)) return byConvention;

        foreach (var item in await cloudFilesService.ListAsync($"{root}/"))
        {
            if (item.Size <= 0 || named.Contains(item.Key)) continue;

            var declared = await DeclaredKindsAsync(item.Key, root);
            if (declared.Contains(kind, System.StringComparer.OrdinalIgnoreCase))
                byConvention.Add(item);
        }

        return byConvention;
    }

    /// <summary>
    /// The kinds one adapter declared. Metadata reads are cached by the installer, and an adapter
    /// whose metadata cannot be read declares nothing rather than taking the whole catalogue down
    /// with it — the list is what an operator needs to configure anything at all.
    /// </summary>
    private async Task<string[]> DeclaredKindsAsync(string key, string root)
    {
        try
        {
            var adapterId = key.StartsWith($"{root}/", System.StringComparison.OrdinalIgnoreCase)
                ? key[(root.Length + 1)..]
                : key;

            // Versioned uploads keep the adapter id one segment up from the version.
            if (Semver.IsVersionNumber(adapterId.Split('/').Last()))
                adapterId = string.Join('/', adapterId.Split('/')[..^1]);

            var metadata = await adapterInstaller.GetMetadataAsync(adapterId);
            if (metadata?.AdapterValues == null) return [];

            return metadata.AdapterValues.TryGetValue("Kind", out var kinds) && !string.IsNullOrWhiteSpace(kinds)
                ? kinds.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                : [];
        }
        catch
        {
            return [];
        }
    }
}
