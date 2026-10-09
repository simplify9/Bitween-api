using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Which of an adapter's required startup properties a caller failed to supply.
/// <para>
/// The answer was written out four times across the subscription validators before this
/// existed — three in Update alone. Create needs the same answer, and six copies of it would
/// be six places for the rule to drift. Whether the adapter runs in-process or in a serverless
/// container is <see cref="AdapterStartupValues"/>'s problem, not this one's.
/// </para>
/// </summary>
public class AdapterRequirements(
    AdapterStartupValues startupValues,
    Services.Adapters.AdapterCatalog catalog = null,
    BitweenOptions bitweenOptions = null)
{
    /// <summary>
    /// Refuses a version pin that cannot run: on a native adapter, which has no versions; on a
    /// slot with no adapter; on a version that was never published or has been withdrawn. And
    /// refuses an adapter — pinned or current — whose manifest asks for a newer Bitween than this
    /// one, when the slot is being changed to it. Nothing is checked for a slot left as it was, so
    /// saving an unrelated field never fails over a choice made before.
    /// </summary>
    public async Task EnsureRunnable(string slot, string adapterId, string version, bool changed)
    {
        if (!changed || string.IsNullOrWhiteSpace(adapterId)) return;

        if (!string.IsNullOrWhiteSpace(version))
        {
            if (NativeAdapterDiscoveryService.IsNative(adapterId))
                throw new SWValidationException("ADAPTER_VERSION",
                    $"The {slot} '{adapterId}' is built in and has no versions to pin.");
            if (catalog != null && !await catalog.HasVersionAsync(adapterId, version))
                throw new SWValidationException("ADAPTER_VERSION",
                    $"Version {version} of the {slot} '{adapterId}' is not published, or has been withdrawn.");
        }

        if (catalog == null) return;
        var manifest = await catalog.ManifestOf(SW.Serverless.Contract.Catalog.AdapterCatalogPaths.Ref(adapterId, version));
        var needs = manifest?.Compatibility?.MinVersionOf(Services.Adapters.AdapterCatalog.Application);
        if (!BitweenInfo.Satisfies(needs, bitweenOptions))
            throw new SWValidationException("ADAPTER_NEEDS_NEWER_BITWEEN",
                $"The {slot} '{adapterId}'{(string.IsNullOrWhiteSpace(version) ? "" : $" {version}")} needs Bitween {needs} or later; this is {BitweenInfo.Version(bitweenOptions)}.");
    }

    /// <param name="adapterId">Native (<c>native:</c> prefix) or serverless. Null/blank means nothing is missing.</param>
    /// <param name="provided">What the caller supplied. Blank values count as not supplied.</param>
    public async Task<IReadOnlyCollection<string>> MissingFor(string adapterId, ICollection<KeyAndValue> provided)
    {
        if (string.IsNullOrEmpty(adapterId)) return Array.Empty<string>();

        // A version that can't be pinned has nothing to describe. Asking for it started the
        // package to read its settings, which threw — a 500 from the request validator, which runs
        // before EnsureRunnable can refuse the version with a reason.
        var (id, version) = SW.Serverless.Contract.Catalog.AdapterCatalogPaths.Split(adapterId);
        if (version != null && catalog != null && !await catalog.HasVersionAsync(id, version))
            return Array.Empty<string>();

        var required = (await startupValues.Describe(adapterId))
            .Where(p => !p.Value.Optional).Select(p => p.Key);

        return required
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .Except((provided ?? Array.Empty<KeyAndValue>())
                .Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key))
            .ToArray();
    }
}
