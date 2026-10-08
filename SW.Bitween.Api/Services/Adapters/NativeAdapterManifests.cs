using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SW.Bitween.NativeAdapters;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// The manifests the native, in-process adapters carry, as embedded <c>adapter.json</c> resources
/// beside their code. They describe and present an adapter the same way a published one's does —
/// display name, summary, publisher, icon, properties — so a catalog or marketplace treats both alike.
/// </summary>
/// <remarks>
/// What a native adapter needs configured is still read from its startup-values type, which is the
/// code the adapter actually runs with; a test keeps the manifests' property lists from drifting
/// away from it.
/// </remarks>
public class NativeAdapterManifests
{
    readonly Dictionary<string, AdapterManifest> manifests;

    public NativeAdapterManifests(IEnumerable<INativeAdapter> nativeAdapters)
    {
        manifests = new Dictionary<string, AdapterManifest>(StringComparer.OrdinalIgnoreCase);
        var assemblies = nativeAdapters.Select(a => a.GetType().Assembly)
            .Append(typeof(INativeAdapter).Assembly)
            .Distinct();

        foreach (var assembly in assemblies)
        foreach (var (id, manifest) in parsed.GetOrAdd(assembly, a => Read(a).ToList()))
            manifests[id] = manifest;
    }

    // Parsed once per assembly: this is resolved per request, and the resources never change.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Assembly, List<(string Id, AdapterManifest Manifest)>> parsed = new();

    /// <summary>The manifest for a native adapter, by its id (its type name); null when it has none.</summary>
    public AdapterManifest Get(string adapterId) =>
        adapterId != null && manifests.TryGetValue(adapterId, out var manifest) ? manifest : null;

    public IReadOnlyCollection<string> Ids => manifests.Keys;

    /// <summary>Every embedded resource named <c>….adapter.json</c>, keyed by the manifest's id.</summary>
    public static IEnumerable<(string Id, AdapterManifest Manifest)> Read(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.EndsWith("." + AdapterManifest.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var manifest = AdapterManifest.Parse(reader.ReadToEnd());
            if (!string.IsNullOrWhiteSpace(manifest.Id)) yield return (manifest.Id, manifest);
        }
    }
}
