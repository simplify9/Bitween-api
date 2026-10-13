using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Marketplace;

/// <summary>A version newer than any installed here, with what its publisher says changed.</summary>
public record RegistryNewerVersion(string Version, string ReleaseNotes);

/// <summary>An installed version the registry has revoked, from its signed revocation list.</summary>
public record RegistryRevokedVersion(string Version, string Reason, string RevokedOn);

/// <summary>What the registry said about one adapter installed from it, when it was last asked.</summary>
/// <param name="Current">The version current here now, read when the check is read rather than when it ran.</param>
/// <param name="Listed">False when the registry no longer lists the package; revocations still apply.</param>
public record RegistryAdapterCheck(string AdapterId, string Current, string Latest, bool UpdateAvailable,
    IReadOnlyList<RegistryNewerVersion> Newer, IReadOnlyList<RegistryRevokedVersion> Revoked, bool Listed,
    DateTime CheckedOn);

/// <summary>The last check, and what it found for each adapter installed from the registry.</summary>
public record RegistryCheckSummary(bool Configured, DateTime? CheckedOn, string Error, IReadOnlyList<RegistryAdapterCheck> Adapters);

/// <summary>
/// Asks the registry, once a day and when someone asks, about every adapter installed from it: is
/// there a newer version and what changed in it, and has an installed version been revoked. What
/// it finds is kept in AdapterStates, so every node shows the same, and the Adapters page reads it
/// without reaching the registry.
/// </summary>
/// <remarks>
/// Revocations are read only from the registry's signed list, checked against the trusted keys,
/// as installing does. A list that doesn't verify changes nothing: the flags from the last good
/// check stay, and the check says why it failed. Nothing is stopped or changed by a revocation;
/// it is shown, and what to do about it is the operator's call.
/// </remarks>
public class RegistryUpdates(BitweenDbContext dbContext, RegistryClient registry, BitweenOptions options,
    AdapterCatalog catalog, ICloudFilesService cloudFiles, ServerlessOptions serverlessOptions)
{
    /// <summary>The rows are kept under this instance key, apart from any adapter's own state.</summary>
    public const string InstanceKey = "registry";
    const string CheckName = "check";
    const string SummaryAdapter = "bitween.marketplace";
    const string SummaryName = "last-check";

    /// <summary>How many newer versions' notes are kept per adapter, newest first.</summary>
    const int NewerKept = 5;
    const int NotesKept = 600;

    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    record Summary(DateTime CheckedOn, string Error);

    public async Task<RegistryCheckSummary> ReadAsync()
    {
        var rows = await dbContext.Set<AdapterState>().AsNoTracking()
            .Where(s => s.InstanceKey == InstanceKey && (s.Name == CheckName || s.Name == SummaryName))
            .ToListAsync();
        var summary = rows.FirstOrDefault(r => r.AdapterId == SummaryAdapter && r.Name == SummaryName) is { } s
            ? JsonSerializer.Deserialize<Summary>(s.Value, Json)
            : null;
        var adapters = new List<RegistryAdapterCheck>();
        foreach (var row in rows.Where(r => r.Name == CheckName).OrderBy(r => r.AdapterId, StringComparer.Ordinal))
        {
            var check = JsonSerializer.Deserialize<RegistryAdapterCheck>(row.Value, Json);
            adapters.Add(check with { Current = (await catalog.GetAsync(check.AdapterId))?.Current });
        }
        return new RegistryCheckSummary(registry.Configured, summary?.CheckedOn, summary?.Error, adapters);
    }

    /// <summary>When the last check ran, whether it worked or not; null if none has.</summary>
    public async Task<DateTime?> LastCheckedOnAsync() =>
        (await dbContext.Set<AdapterState>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.AdapterId == SummaryAdapter && s.InstanceKey == InstanceKey && s.Name == SummaryName))
        ?.UpdatedOn;

    /// <summary>Asks the registry now. A registry that can't be reached is recorded, not thrown.</summary>
    public async Task<RegistryCheckSummary> CheckAsync(CancellationToken cancellation = default)
    {
        if (!registry.Configured) return await ReadAsync();

        var now = DateTime.UtcNow;
        string error = null;
        try
        {
            // Nothing installed from it: nothing to ask, and the registry isn't reached at all.
            var entries = await InstalledFromRegistryAsync();
            var revoked = entries.Count == 0 ? [] : await RevocationsAsync();
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                await SaveAsync(entry.Id, CheckName, await CheckOneAsync(entry, revoked, now));
            }

            // An adapter deleted, or no longer from the registry, keeps no flags.
            var ids = entries.Select(e => e.Id).ToList();
            await dbContext.Set<AdapterState>()
                .Where(s => s.InstanceKey == InstanceKey && s.Name == CheckName && !ids.Contains(s.AdapterId))
                .ExecuteDeleteAsync(cancellation);
        }
        catch (Exception ex) when (ex is SWValidationException or SWNotFoundException or JsonException)
        {
            error = ex is SWNotFoundException ? "The registry has no revocation list at its address." : ex.Message;
        }

        await SaveAsync(SummaryAdapter, SummaryName, new Summary(now, error));
        await dbContext.SaveChangesAsync(cancellation);
        return await ReadAsync();
    }

    async Task<RegistryAdapterCheck> CheckOneAsync(AdapterCatalogEntry entry, IReadOnlyList<JsonElement> revocations, DateTime now)
    {
        var installed = entry.Versions.Where(v => !v.Withdrawn).ToList();
        var revoked = installed
            .Select(v => (v, r: revocations.FirstOrDefault(r =>
                (Text(r, "packageId") == entry.Id && Text(r, "version") == v.Version)
                || (!string.IsNullOrEmpty(v.Sha256) && string.Equals(Text(r, "sha256"), v.Sha256, StringComparison.OrdinalIgnoreCase)))))
            .Where(x => x.r.ValueKind == JsonValueKind.Object)
            .Select(x => new RegistryRevokedVersion(x.v.Version, Text(x.r, "reason"), Text(x.r, "revokedOn")))
            .ToList();

        RegistryPackage package;
        try
        {
            package = await registry.PackageAsync(entry.Id);
        }
        catch (SWNotFoundException)
        {
            return new RegistryAdapterCheck(entry.Id, entry.Current, null, false, [], revoked, false, now);
        }

        var highest = installed.Select(v => v.Version).Where(Semver.IsVersionNumber).OrderByDescending(Semver.ParseVersion).FirstOrDefault();
        var newer = package.Versions
            .Where(v => string.Equals(v.Status, "Published", StringComparison.OrdinalIgnoreCase) && Semver.IsVersionNumber(v.Version))
            .Where(v => highest == null || Semver.ParseVersion(v.Version).CompareTo(Semver.ParseVersion(highest)) > 0)
            .OrderByDescending(v => Semver.ParseVersion(v.Version))
            .Take(NewerKept)
            .ToList();

        var notes = new List<RegistryNewerVersion>();
        foreach (var v in newer)
        {
            string text = null;
            try
            {
                var detail = await registry.VersionAsync(entry.Id, v.Version);
                text = AdapterManifest.Parse(detail.Manifest.GetRawText())?.ReleaseNotes;
            }
            catch (SWNotFoundException)
            {
                // Listed a moment ago and gone now: its number is enough.
            }
            notes.Add(new RegistryNewerVersion(v.Version, text?.Length > NotesKept ? text[..NotesKept] + "…" : text));
        }

        return new RegistryAdapterCheck(entry.Id, entry.Current, newer.FirstOrDefault()?.Version ?? package.Card.Latest, newer.Count > 0,
            notes, revoked, true, now);
    }

    /// <summary>The registry's revocation list, once its signature has been checked.</summary>
    async Task<IReadOnlyList<JsonElement>> RevocationsAsync()
    {
        var (trusted, _) = RegistrySignatures.TrustedKeys(options.RegistryTrustedKeys);
        if (trusted.Count == 0)
            throw new SWValidationException("REGISTRY_KEYS",
                "No registry signing keys are set, so the revocation list can't be checked. Add them under Settings, Marketplace.");

        var document = await registry.RevocationsAsync();
        if (!RegistrySignatures.VerifyAny(trusted, RegistrySignatures.RevocationsMessage(document.Payload), document.Signature))
            throw new SWValidationException("REGISTRY_SIGNATURE",
                "The registry's revocation list isn't signed by a trusted key, so nothing was changed.");

        using var parsed = JsonDocument.Parse(document.Payload);
        return parsed.RootElement.TryGetProperty("revoked", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => e.Clone()).ToList()
            : [];
    }

    /// <summary>Every catalog entry that came from a registry.</summary>
    async Task<List<AdapterCatalogEntry>> InstalledFromRegistryAsync()
    {
        var root = AdapterCatalogPaths.CatalogRoot(serverlessOptions.AdapterRemotePath);
        IEnumerable<CloudFileInfo> files;
        try
        {
            files = await cloudFiles.ListAsync(root);
        }
        catch
        {
            // No catalog yet: nothing installed from anywhere.
            return [];
        }

        var ids = files.Where(f => f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Key[root.Length..^".json".Length])
            .Where(id => id.Length > 0 && !id.Contains('/'));
        var entries = new List<AdapterCatalogEntry>();
        foreach (var id in ids)
            if (await catalog.GetAsync(id) is { } entry && entry.Extensions?.ContainsKey(MarketplaceInstaller.OriginExtension) == true)
                entries.Add(entry);
        return entries;
    }

    async Task SaveAsync(string adapterId, string name, object value)
    {
        var text = JsonSerializer.Serialize(value, Json);
        var row = await dbContext.Set<AdapterState>()
            .SingleOrDefaultAsync(s => s.AdapterId == adapterId && s.InstanceKey == InstanceKey && s.Name == name);
        if (row == null)
            dbContext.Add(new AdapterState { AdapterId = adapterId, InstanceKey = InstanceKey, Name = name, Value = text, UpdatedOn = DateTime.UtcNow });
        else
        {
            row.Value = text;
            row.UpdatedOn = DateTime.UtcNow;
        }
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Runs <see cref="RegistryUpdates"/> once a day on whichever node gets there first. Every node
/// looks hourly; the last check's time, in the database, keeps them from all asking. Two nodes
/// asking at once only ask the registry twice: the check reads and writes nothing else.
/// </summary>
public class RegistryUpdateChecker(IServiceScopeFactory scopes, ILogger<RegistryUpdateChecker> logger) : BackgroundService
{
    static readonly TimeSpan LookEvery = TimeSpan.FromHours(1);
    static readonly TimeSpan FirstLook = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstLook, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var registry = scope.ServiceProvider.GetRequiredService<RegistryClient>();
                var updates = scope.ServiceProvider.GetRequiredService<RegistryUpdates>();
                if (registry.Configured && (await updates.LastCheckedOnAsync() is not { } last || DateTime.UtcNow - last >= RegistryUpdates.Every))
                {
                    var result = await updates.CheckAsync(stoppingToken);
                    if (result.Error != null) logger.LogWarning("The daily registry check didn't finish: {Error}", result.Error);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Installed adapters keep running without it; only the flags are a day older.
                logger.LogWarning(ex, "The daily registry check failed.");
            }

            try { await Task.Delay(LookEvery, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
