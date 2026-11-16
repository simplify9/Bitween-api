using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Services;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Resources.Adapters;

public class AdapterManifestRequest
{
    public string AdapterId { get; set; }

    /// <summary>A published version; the current one when left out.</summary>
    public string Version { get; set; }
}

/// <summary>
/// One adapter version's manifest, whole: what <see cref="Catalog"/> picks fields from, with every
/// section Bitween itself does not read - a host's own extension sections included - kept as written.
/// </summary>
/// <remarks>
/// For hosts that configure subscriptions and need more of an adapter's description than the
/// catalog passes on. Read from the catalog only: an adapter with no manifest, a version that was
/// never published and a withdrawn one are all not found. Nothing is started to describe it.
/// </remarks>
[HandlerName("manifest")]
public class Manifest(
    AdapterCatalog catalog,
    NativeAdapterManifests nativeManifests,
    BitweenDbContext dbContext,
    RequestContext requestContext) : IQueryHandler<AdapterManifestRequest, object>
{
    public async Task<object> Handle(AdapterManifestRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        if (string.IsNullOrWhiteSpace(request.AdapterId)) throw new SWValidationException("AdapterId", "Say which adapter.");

        var manifest = await Find(request.AdapterId.Trim(), string.IsNullOrWhiteSpace(request.Version) ? null : request.Version.Trim())
                       ?? throw new SWNotFoundException($"{request.AdapterId} {request.Version}".Trim());
        return ToToken(manifest);
    }

    async Task<AdapterManifest> Find(string adapterId, string version)
    {
        if (NativeAdapterDiscoveryService.IsNative(adapterId))
        {
            // A built-in adapter has the one version it was built with.
            var native = nativeManifests.Get(adapterId);
            return version == null || string.Equals(native?.Version, version, StringComparison.OrdinalIgnoreCase) ? native : null;
        }

        var entry = await catalog.GetAsync(adapterId.ToLowerInvariant());
        if (entry == null) return null;
        if (version == null) return entry.Manifest;
        return entry.Find(version) is { Withdrawn: false } published ? published.Manifest : null;
    }

    /// <summary>
    /// The manifest as its own serialiser writes it - camel-cased, extension sections at the level
    /// they were found - in a form the API's serialiser passes through untouched. Dates are left as
    /// the text they were written as, not read back as dates and reformatted.
    /// </summary>
    internal static JToken ToToken(AdapterManifest manifest)
    {
        using var reader = new JsonTextReader(new StringReader(manifest.ToJson())) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }
}
