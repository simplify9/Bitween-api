using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// The published adapters' catalog — current version, every version with its manifest, the icon —
/// and the manifests of the native ones, behind one lookup.
/// </summary>
/// <remarks>
/// A published adapter has a catalog entry only once it has been published by an installer that
/// writes one. Every answer here is therefore optional, and every caller falls back to what it did
/// before catalogs existed: list the package files, describe the adapter by asking it.
/// </remarks>
public class AdapterCatalog(
    ICloudFilesService cloudFiles,
    ServerlessOptions serverlessOptions,
    NativeAdapterManifests nativeManifests,
    IMemoryCache cache)
{
    /// <summary>
    /// What adapters call Bitween in their manifest's compatibility.applications — the minimum
    /// Bitween they need — and what manifests before that field called minBitweenVersion.
    /// </summary>
    public const string Application = "bitween";

    // Long enough that a screen listing every kind reads each entry once; short enough that a
    // publish shows up without anyone thinking to clear anything.
    static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    AdapterCatalogStore Store => new(cloudFiles, serverlessOptions.AdapterRemotePath);

    static string CacheKey(string adapterId) => $"bitween.adapter-catalog.{adapterId.ToLowerInvariant()}";

    /// <summary>The adapter's catalog entry, or null when it has none — native, or published before catalogs.</summary>
    public async Task<AdapterCatalogEntry> GetAsync(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId) || NativeAdapterDiscoveryService.IsNative(adapterId)) return null;

        if (cache.TryGetValue(CacheKey(adapterId), out AdapterCatalogEntry cached)) return cached;

        AdapterCatalogEntry entry;
        try
        {
            entry = await Store.GetAsync(adapterId);
        }
        catch
        {
            // A catalog that cannot be read is treated as absent: everything that uses it has a
            // fallback, and a storage hiccup must not empty the adapter lists.
            return null;
        }

        cache.Set(CacheKey(adapterId), entry, CacheFor);
        return entry;
    }

    public void Forget(string adapterId)
    {
        if (!string.IsNullOrWhiteSpace(adapterId)) cache.Remove(CacheKey(adapterId));
    }

    /// <summary>
    /// The manifest of what <paramref name="adapterRef"/> runs: a native adapter's own, a pinned
    /// version's, or the current package's. Null when there is none to read.
    /// </summary>
    public async Task<AdapterManifest> ManifestOf(string adapterRef)
    {
        if (string.IsNullOrWhiteSpace(adapterRef)) return null;
        if (NativeAdapterDiscoveryService.IsNative(adapterRef)) return nativeManifests.Get(adapterRef);

        var (adapterId, version) = AdapterCatalogPaths.Split(adapterRef);
        var entry = await GetAsync(adapterId);
        if (entry == null) return null;
        return version == null ? entry.Manifest : entry.Find(version)?.Manifest;
    }

    /// <summary>
    /// Whether <paramref name="version"/> of the adapter can be pinned: published and not withdrawn.
    /// Without a catalog entry, published means its package exists.
    /// </summary>
    public async Task<bool> HasVersionAsync(string adapterId, string version)
    {
        if (!AdapterCatalogPaths.IsVersion(version)) return false;

        var entry = await GetAsync(adapterId);
        if (entry != null)
            return entry.Find(version) is { Withdrawn: false };

        // Where versions are published now, or where an installer from before the versions prefix put them.
        foreach (var key in new[]
                 {
                     AdapterCatalogPaths.Version(serverlessOptions.AdapterRemotePath, adapterId, version),
                     AdapterCatalogPaths.LegacyVersion(serverlessOptions.AdapterRemotePath, adapterId, version)
                 })
            if ((await cloudFiles.ListAsync(key)).Any(f => string.Equals(f.Key, key, StringComparison.Ordinal) && f.Size > 0))
                return true;
        return false;
    }
}
