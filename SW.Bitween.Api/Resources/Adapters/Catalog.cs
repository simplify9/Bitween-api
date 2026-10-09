using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

/// <summary>
/// Every adapter of one kind, each with the startup properties it expects.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SearchVersioned"/> answers with the adapters alone, which left a caller that needs to
/// draw a form per adapter to ask <see cref="GetStartupValues"/> once per row. On a screen listing
/// all four kinds that is around ninety requests, six of which a browser will run at a time, so the
/// last of them waits behind fifteen rounds of queueing — and every one of those requests carries
/// its own permission check. Answering the whole kind at once makes it four requests for the screen.
/// </para>
/// <para>
/// <see cref="SearchVersioned"/> is deliberately left as it is: the older UI reads it, does not need
/// the properties, and should not start paying for them.
/// </para>
/// </remarks>
[HandlerName("Catalog")]
public class Catalog(
    AdapterListing listing,
    AdapterStartupValues startupValues,
    BitweenDbContext dbContext,
    RequestContext requestContext) : IQueryHandler<AdapterSearchRequest, object>
{
    public async Task<object> Handle(AdapterSearchRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var adapters = await listing.List(request.Prefix);

        // Asked for all at once and left unthrottled on purpose. The native ones answer by
        // reflection and should not be made to queue, and how many published adapters may be
        // running at a time is capped in ServerlessAdapterDescriber — process-wide, which is the
        // only place it can be, since this handler knows nothing of the other requests in flight.
        var described = await Task.WhenAll(adapters.Select(async a => (a.Key, Values: await Describe(a.Key))));
        var byKey = described.ToDictionary(d => d.Key, d => d.Values);

        return adapters.Select(a => new
        {
            a.Key,
            a.Native,
            // Just the version numbers. VersionPaths carries each one as a path, which is what the
            // older shape passed through and what made a version read as an object rather than
            // "1.2.3" to anything trying to label it.
            // Old-layout version files, and the catalog's versions that can still be pinned — so the
            // version label an older client shows is right for adapters published either way.
            Versions = a.VersionPaths.Select(v => v.Split('/').Last())
                .Concat(a.Catalog?.Versions.Where(v => !v.Withdrawn).Select(v => v.Version) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => System.Version.TryParse(v.Split('-')[0], out var parsed) ? parsed : new System.Version())
                .ToList(),
            StartupValues = byKey[a.Key],

            // Additive: a client that predates the catalog ignores all of these. Every one is null
            // or empty for an adapter published before manifests existed.
            CurrentVersion = a.Catalog?.Current,
            VersionHistory = a.Catalog?.Versions
                .Select(v => new
                {
                    v.Version,
                    v.PublishedOn,
                    v.PublishedBy,
                    v.Withdrawn,
                    v.Manifest?.ReleaseNotes,
                    MinBitweenVersion = v.Manifest?.Compatibility?.MinVersionOf(Services.Adapters.AdapterCatalog.Application),
                    // Whether the package carries its source, so a client offers to show it
                    // only where there is something to show.
                    HasSource = v.Manifest?.Source?.Files is { Count: > 0 },
                    // Which the editor can open: Python and Node versions with their source.
                    v.Manifest?.Runtime
                })
                .ToList(),
            DisplayName = a.Manifest?.DisplayName,
            a.Manifest?.Summary,
            a.Manifest?.Description,
            Publisher = a.Manifest?.Publisher?.Name,
            PublisherUrl = a.Manifest?.Publisher?.Url,
            a.Manifest?.License,
            a.Manifest?.Homepage,
            a.Manifest?.Repository,
            Tags = a.Manifest?.Tags ?? [],
            Categories = a.Manifest?.Categories ?? [],
            a.Manifest?.Language,
            a.Manifest?.Runtime,
            a.Manifest?.Lifecycle,
            Icon = a.Catalog?.IconDataUri,
            MinBitweenVersion = a.Manifest?.Compatibility?.MinVersionOf(Services.Adapters.AdapterCatalog.Application)
        });
    }

    private async Task<IDictionary<string, StartupValue>> Describe(string adapterId)
    {
        try
        {
            return AdapterStartupValues.WithoutSecretDefaults(await startupValues.Describe(adapterId));
        }
        catch (Exception)
        {
            // One adapter that cannot be described — its runtime is missing locally, say — must not
            // blank out the rest of the catalogue, including the native ones that resolved
            // perfectly well. It comes back with no properties, as it did when the caller was
            // asking row by row and swallowing the failure itself.
            return new Dictionary<string, StartupValue>();
        }
    }
}
