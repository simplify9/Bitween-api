using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Marketplace;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Resources.Marketplace;

public class MarketplaceSearchRequest
{
    public string Q { get; set; }
    public string Tier { get; set; }
}

public class MarketplacePackageRequest
{
    public string Id { get; set; }
}

public class MarketplaceInstallRequest
{
    public string Id { get; set; }
    public string Version { get; set; }
    public bool Current { get; set; }
}

/// <summary>What this Bitween already has of a registry package.</summary>
public record InstalledState(bool Installed, bool FromRegistry, string Current, IReadOnlyList<string> Versions);

/// <summary>
/// The Marketplace tab: what the registry lists, with what this Bitween has of each. With no
/// registry address set it says the marketplace is off and fetches nothing.
/// </summary>
[HandlerName("search")]
public class Search(BitweenDbContext dbContext, RequestContext requestContext, RegistryClient registry, AdapterCatalog catalog)
    : IQueryHandler<MarketplaceSearchRequest, object>
{
    public async Task<object> Handle(MarketplaceSearchRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        if (!registry.Configured) return new { Configured = false };

        var found = await registry.SearchAsync(request.Q, request.Tier);
        var result = new List<object>();
        foreach (var card in found.Result)
            result.Add(new { card, installed = await Installed.Of(catalog, card.Id) });
        return new { Configured = true, Registry = registry.BaseUrl, Result = result, found.TotalCount };
    }
}

/// <summary>One registry package with its versions, and what this Bitween has of it.</summary>
[HandlerName("package")]
public class Package(BitweenDbContext dbContext, RequestContext requestContext, RegistryClient registry, AdapterCatalog catalog)
    : IQueryHandler<MarketplacePackageRequest, object>
{
    public async Task<object> Handle(MarketplacePackageRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        if (string.IsNullOrWhiteSpace(request.Id)) throw new SWValidationException("Id", "Say which package.");

        var id = request.Id.Trim().ToLowerInvariant();
        var package = await registry.PackageAsync(id);
        var latest = package.Card.Latest == null ? null : await registry.VersionAsync(id, package.Card.Latest);
        return new { package, latest = latest?.Manifest, installed = await Installed.Of(catalog, id) };
    }
}

/// <summary>Installs a version from the registry, after checking its signatures. In the audit trail.</summary>
[HandlerName("install")]
public class Install(BitweenDbContext dbContext, RequestContext requestContext, MarketplaceInstaller installer)
    : ICommandHandler<MarketplaceInstallRequest, object>
{
    public async Task<object> Handle(MarketplaceInstallRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        var who = requestContext.GetNameIdentifier();

        var installed = await installer.InstallAsync(request.Id, request.Version, request.Current, who);
        dbContext.Add(new AdapterRelease(installed.AdapterId, installed.Version, AdapterRelease.InstalledAction, null, who));
        if (installed.Current)
            dbContext.Add(new AdapterRelease(installed.AdapterId, installed.Version, AdapterRelease.PromotedAction, null, who));
        await dbContext.SaveChangesAsync();
        return installed;
    }
}

static class Installed
{
    public static async Task<InstalledState> Of(AdapterCatalog catalog, string id)
    {
        var entry = await catalog.GetAsync(id);
        if (entry == null) return new InstalledState(false, false, null, []);
        var fromRegistry = entry.Extensions?.ContainsKey(MarketplaceInstaller.OriginExtension) == true;
        var versions = entry.Versions.Where(v => !v.Withdrawn).Select(v => v.Version).ToList();
        return new InstalledState(true, fromRegistry, entry.Current, versions);
    }
}
