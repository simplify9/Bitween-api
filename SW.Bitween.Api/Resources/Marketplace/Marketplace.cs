using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using SW.Bitween.Domain;
using SW.Bitween.Services;
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
/// The Marketplace tab: what the registry lists, with what this Bitween has of each. Turned off,
/// or with no registry address, it says so and fetches nothing.
/// </summary>
[HandlerName("search")]
public class Search(BitweenDbContext dbContext, RequestContext requestContext, RegistryClient registry, AdapterCatalog catalog)
    : IQueryHandler<MarketplaceSearchRequest, object>
{
    public async Task<object> Handle(MarketplaceSearchRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        if (!registry.Configured) return new { Configured = false, registry.Disabled };

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

public class MarketplaceKitRequest
{
    public string Id { get; set; }
    public string Version { get; set; }
}

public class MarketplaceKitInstallRequest
{
    public string Id { get; set; }
    public string Version { get; set; }

    /// <summary>What this installation is called; the kit's id unless given, another name for a second one.</summary>
    public string Name { get; set; }

    public Dictionary<string, string> Parameters { get; set; }
}

/// <summary>A kit's connectors and the parameters its install form asks for.</summary>
[HandlerName("kit")]
public class Kit(BitweenDbContext dbContext, RequestContext requestContext, KitInstaller kits)
    : IQueryHandler<MarketplaceKitRequest, object>
{
    public async Task<object> Handle(MarketplaceKitRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        return await kits.DescribeAsync(request.Id, request.Version);
    }
}

/// <summary>What installing a kit would do, changing nothing.</summary>
[HandlerName("kitplan")]
public class KitPlan(BitweenDbContext dbContext, RequestContext requestContext, KitInstaller kits)
    : ICommandHandler<MarketplaceKitInstallRequest, object>
{
    public async Task<object> Handle(MarketplaceKitInstallRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        return await kits.PlanAsync(request.Id, request.Version, request.Name, request.Parameters);
    }
}

/// <summary>
/// Installs a kit: its connectors, then its configuration with the parameters filled in, as the caller,
/// so each change needs the caller's own permissions and is in the trail under their name.
/// </summary>
[HandlerName("kitinstall")]
public class KitInstall(BitweenDbContext dbContext, RequestContext requestContext, KitInstaller kits)
    : ICommandHandler<MarketplaceKitInstallRequest, object>
{
    public async Task<object> Handle(MarketplaceKitInstallRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        var run = await kits.InstallAsync(request.Id, request.Version, request.Name, request.Parameters);
        if (run.Applied)
            await dbContext.RecordNowAsync(requestContext, Domain.OperatorAction.InstallKit, $"{request.Id} {request.Version}",
                $"installed as {run.Name}, {run.Changes} change(s)");
        return run;
    }
}

public class MarketplaceUpdatesRequest
{
}

/// <summary>
/// What the last registry check found for the adapters installed from it: newer versions with their
/// release notes, and installed versions the registry has revoked. Read from what was stored, so it
/// answers with the registry unreachable.
/// </summary>
[HandlerName("updates")]
public class Updates(BitweenDbContext dbContext, RequestContext requestContext, RegistryUpdates updates)
    : IQueryHandler<MarketplaceUpdatesRequest, object>
{
    public async Task<object> Handle(MarketplaceUpdatesRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        return await updates.ReadAsync();
    }
}

/// <summary>Asks the registry now, rather than waiting for the daily check.</summary>
[HandlerName("check")]
public class Check(BitweenDbContext dbContext, RequestContext requestContext, RegistryUpdates updates)
    : ICommandHandler<MarketplaceUpdatesRequest, object>
{
    public async Task<object> Handle(MarketplaceUpdatesRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        return await updates.CheckAsync();
    }
}

public class MarketplaceRegisterRequest
{
    /// <summary>The registration code Simplify9 gave, bwreg_...</summary>
    public string Code { get; set; }

    /// <summary>What to call this Bitween in the registry; its address by default.</summary>
    public string DisplayName { get; set; }
}

/// <summary>
/// Registers this Bitween with the registry: the code becomes an instance id and a token, saved as
/// settings, and the licences are fetched. Needed only for private or paid connectors and usage reports.
/// </summary>
[HandlerName("register")]
public class Register(BitweenDbContext dbContext, RequestContext requestContext, RegistryClient registry, BitweenOptions options,
    SettingsService settings, IInfolinkCache cache, RegistryLicences licences)
    : ICommandHandler<MarketplaceRegisterRequest, object>
{
    public async Task<object> Handle(MarketplaceRegisterRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);
        if (string.IsNullOrWhiteSpace(request?.Code))
            throw new SWValidationException("Code", "Paste the registration code Simplify9 gave.");
        var token = SettingsCatalog.Find("Bitween.RegistryInstanceToken");
        if (!settings.CanStore(token))
            throw new SWValidationException("SETTING_ENCRYPTION_UNAVAILABLE",
                $"The instance token is a secret and can only be stored once {BitweenOptions.ConfigurationSection}:{nameof(BitweenOptions.SettingsEncryptionKey)} is configured.");

        var registration = await registry.RegisterAsync(request.Code.Trim(),
            string.IsNullOrWhiteSpace(request.DisplayName) ? options.PublicUrl ?? "Bitween" : request.DisplayName.Trim(),
            BitweenInfo.Version(options).ToString());

        // Stored and applied the way Settings, Update does, so the next request already sends the token.
        foreach (var (definition, value) in new[] { (SettingsCatalog.Find("Bitween.RegistryInstanceId"), registration.InstanceId), (token, registration.Token) })
        {
            var stored = await dbContext.Set<Setting>().FindAsync(definition.Key);
            var toStore = settings.ToStored(definition, value);
            if (stored is null) dbContext.Add(new Setting { Id = definition.Key, Value = toStore });
            else stored.Value = toStore;
            settings.Apply(definition, value);
        }
        await dbContext.SaveChangesAsync();
        await cache.BroadcastRevoke();

        var summary = await licences.RefreshAsync();
        return new { registration.InstanceId, registration.DisplayName, registration.CustomerName, Licences = summary };
    }
}

public class MarketplaceLicencesRequest
{
}

/// <summary>The licences this Bitween holds, as last fetched; nothing is asked of the registry.</summary>
[HandlerName("licences")]
public class Licences(BitweenDbContext dbContext, RequestContext requestContext, RegistryLicences licences)
    : IQueryHandler<MarketplaceLicencesRequest, object>
{
    public async Task<object> Handle(MarketplaceLicencesRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        return await licences.ReadAsync();
    }
}

/// <summary>Fetches the licences from the registry now.</summary>
[HandlerName("refreshlicences")]
public class RefreshLicences(BitweenDbContext dbContext, RequestContext requestContext, RegistryLicences licences)
    : ICommandHandler<MarketplaceLicencesRequest, object>
{
    public async Task<object> Handle(MarketplaceLicencesRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        return await licences.RefreshAsync();
    }
}

public class MarketplaceUsageRequest
{
    /// <summary>The day to report, yyyy-MM-dd; yesterday by default.</summary>
    public string Day { get; set; }

    /// <summary>True sends it; false only shows what would be sent.</summary>
    public bool Send { get; set; }
}

/// <summary>What the daily usage report says, and sends it when asked.</summary>
[HandlerName("usage")]
public class Usage(BitweenDbContext dbContext, RequestContext requestContext, UsageReporter usage)
    : ICommandHandler<MarketplaceUsageRequest, object>
{
    public async Task<object> Handle(MarketplaceUsageRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);
        var day = string.IsNullOrWhiteSpace(request?.Day) ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
            : DateOnly.TryParseExact(request.Day, "yyyy-MM-dd", out var d) ? d : throw new SWValidationException("Day", "Give the day as yyyy-MM-dd.");
        return request?.Send == true ? await usage.SendAsync(day) : await usage.PreviewAsync(day);
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
