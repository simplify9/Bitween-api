using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Marketplace;

/// <summary>A licence as this Bitween holds it: the registry's document, already checked against the trusted keys.</summary>
public record RegistryLicence(int LicenceId, string Customer, IReadOnlyList<string> Instances, IReadOnlyList<string> Packages,
    DateTime Starts, DateTime Ends, int GraceDays, DateTime IssuedOn)
{
    /// <summary>Whether it names this instance: by id, or any.</summary>
    public bool Names(string instanceId) => Instances.Any(i => i == "*" || string.Equals(i, instanceId, StringComparison.Ordinal));

    /// <summary>Whether it covers a package: an exact id, a namespace pattern (<c>s9.*</c>), or everything.</summary>
    public bool Covers(string packageId) => Packages.Any(p =>
        p == "*" || (p.EndsWith(".*", StringComparison.Ordinal)
            ? packageId.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(p, packageId, StringComparison.OrdinalIgnoreCase)));

    public LicenceState StateAt(DateTime now) =>
        now < Starts ? LicenceState.NotYet : now <= Ends ? LicenceState.Live : now <= Ends.AddDays(GraceDays) ? LicenceState.Grace : LicenceState.Expired;
}

public enum LicenceState { None, NotYet, Live, Grace, Expired }

/// <summary>What a package's licence situation is here, now.</summary>
public record LicenceStatus(LicenceState State, RegistryLicence Licence)
{
    /// <summary>Whether the connector may be installed and may run.</summary>
    public bool Allows => State is LicenceState.Live or LicenceState.Grace;
}

/// <summary>What was read for the Settings page: when the licences were last fetched, and each one's state.</summary>
public record RegistryLicenceSummary(bool Registered, string InstanceId, DateTime? RefreshedOn, string Error, IReadOnlyList<RegistryLicenceRow> Licences);

public record RegistryLicenceRow(int LicenceId, string Customer, string Packages, string Instances, DateTime Starts, DateTime Ends, int GraceDays, LicenceState State);

/// <summary>
/// The licences the registry issued to this Bitween, kept here so a paid connector installs and runs
/// with no call to the registry: fetched by the daily check (and on demand), each checked against the
/// trusted keys as a countersignature is, and stored in AdapterStates so every node reads the same.
/// A licence that doesn't verify is dropped and said so; the rest are kept. Nothing here reaches the
/// registry when a connector runs.
/// </summary>
/// <remarks>
/// Decided 2026-10-12: once a licence has expired, past its grace, the paid connector stops: calls
/// through it fail with a licence error a retry policy can retry once the licence is renewed. Nothing
/// else on the instance is affected, and a free connector never asks.
/// </remarks>
public class RegistryLicences(BitweenDbContext dbContext, RegistryClient registry, BitweenOptions options, AdapterCatalog catalog, IMemoryCache memory)
{
    public const string InstanceKey = RegistryUpdates.InstanceKey;
    const string Adapter = "bitween.marketplace";
    const string Name = "licences";
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    record Stored(DateTime RefreshedOn, string Error, List<RegistryLicence> Licences);

    /// <summary>Fetches the licences from the registry and keeps the ones that verify. Returns what is held now.</summary>
    public async Task<RegistryLicenceSummary> RefreshAsync()
    {
        if (!registry.Configured || !registry.Registered) return await ReadAsync();
        var (trusted, _) = RegistrySignatures.TrustedKeys(options.RegistryTrustedKeys);
        var kept = new List<RegistryLicence>();
        string error = null;
        try
        {
            if (trusted.Count == 0)
                throw new SWValidationException("REGISTRY_KEYS", "No registry signing keys are set, so no licence can be checked. Add them under Settings, Marketplace.");
            var documents = await registry.EntitlementsAsync();
            var dropped = 0;
            foreach (var document in documents)
            {
                if (!RegistrySignatures.VerifyAny(trusted, RegistrySignatures.LicenceMessage(document.Payload), document.Signature)) { dropped++; continue; }
                var licence = Parse(document.Payload);
                if (licence == null) { dropped++; continue; }
                kept.Add(licence);
            }
            if (dropped > 0) error = $"{dropped} licence(s) the registry sent didn't verify with the trusted keys and were not kept.";
        }
        catch (Exception ex) when (ex is SWValidationException or SWNotFoundException or JsonException)
        {
            // What was held stays: the registry being unreachable doesn't stop a paid connector.
            var held = await LoadAsync();
            kept = held?.Licences ?? [];
            error = ex is SWNotFoundException ? "The registry doesn't know this instance; register it again." : ex.Message;
        }
        await SaveAsync(new Stored(DateTime.UtcNow, error, kept));
        memory.Remove(CacheKey);
        return await ReadAsync();
    }

    public async Task<RegistryLicenceSummary> ReadAsync()
    {
        var stored = await LoadAsync();
        var now = DateTime.UtcNow;
        return new RegistryLicenceSummary(registry.Registered, options.RegistryInstanceId, stored?.RefreshedOn, stored?.Error,
            (stored?.Licences ?? []).OrderByDescending(l => l.Ends).Select(l => new RegistryLicenceRow(l.LicenceId, l.Customer,
                string.Join(", ", l.Packages), string.Join(", ", l.Instances), l.Starts, l.Ends, l.GraceDays, l.StateAt(now))).ToList());
    }

    /// <summary>The best licence for a package here, now: the one that allows it with the latest end, or the nearest otherwise.</summary>
    public async Task<LicenceStatus> StatusFor(string packageId)
    {
        var licences = await CachedAsync();
        var now = DateTime.UtcNow;
        var mine = licences.Where(l => l.Covers(packageId) && l.Names(options.RegistryInstanceId ?? "")).ToList();
        if (mine.Count == 0) return new LicenceStatus(LicenceState.None, null);
        var best = mine.OrderByDescending(l => l.StateAt(now) is LicenceState.Live or LicenceState.Grace).ThenByDescending(l => l.Ends).First();
        return new LicenceStatus(best.StateAt(now), best);
    }

    /// <summary>
    /// Before a connector runs: a paid one installed from the registry must hold a licence that is live
    /// or in grace, or the call fails with ADAPTER_LICENCE_EXPIRED. Everything else passes without a look.
    /// </summary>
    public async Task EnsureLicensedAsync(string adapterRef)
    {
        if (string.IsNullOrWhiteSpace(adapterRef) || NativeAdapterDiscoveryService.IsNative(adapterRef)) return;
        var (id, _) = AdapterCatalogPaths.Split(adapterRef);
        var paid = await memory.GetOrCreateAsync($"registry.paid:{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var catalogEntry = await catalog.GetAsync(id);
            return catalogEntry?.Extensions != null && catalogEntry.Extensions.TryGetValue(MarketplaceInstaller.OriginExtension, out var origin)
                   && origin.ValueKind == JsonValueKind.Object && origin.TryGetProperty("pricing", out var pricing)
                   && string.Equals(pricing.GetString(), "paid", StringComparison.OrdinalIgnoreCase);
        });
        if (!paid) return;
        var status = await StatusFor(id);
        if (status.Allows) return;
        throw new SWValidationException("ADAPTER_LICENCE_EXPIRED", status.State switch
        {
            LicenceState.Expired => $"The licence for {id} ended on {status.Licence.Ends:yyyy-MM-dd} and its grace is over. Renew it with Simplify9; the call can be retried once it is.",
            LicenceState.NotYet => $"The licence for {id} starts on {status.Licence.Starts:yyyy-MM-dd}.",
            _ => $"This Bitween holds no licence for {id}. Register it under Settings, Marketplace, and ask Simplify9 for one.",
        });
    }

    static RegistryLicence Parse(string payload)
    {
        try
        {
            var e = JsonDocument.Parse(payload).RootElement;
            return new RegistryLicence(
                e.GetProperty("licenceId").GetInt32(),
                e.GetProperty("customer").GetProperty("name").GetString(),
                e.GetProperty("instances").EnumerateArray().Select(i => i.GetString()).ToList(),
                e.GetProperty("packages").EnumerateArray().Select(p => p.GetString()).ToList(),
                DateTime.Parse(e.GetProperty("starts").GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                DateTime.Parse(e.GetProperty("ends").GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                e.TryGetProperty("graceDays", out var g) ? g.GetInt32() : 0,
                e.TryGetProperty("issuedOn", out var issued) ? DateTime.Parse(issued.GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal) : DateTime.MinValue);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    const string CacheKey = "registry.licences";

    Task<List<RegistryLicence>> CachedAsync() =>
        memory.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return (await LoadAsync())?.Licences ?? [];
        });

    async Task<Stored> LoadAsync()
    {
        var row = await dbContext.Set<AdapterState>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.AdapterId == Adapter && s.InstanceKey == InstanceKey && s.Name == Name);
        if (row == null) return null;
        try { return JsonSerializer.Deserialize<Stored>(row.Value, Json); }
        catch (JsonException) { return null; }
    }

    async Task SaveAsync(Stored stored)
    {
        var text = JsonSerializer.Serialize(stored, Json);
        var row = await dbContext.Set<AdapterState>().SingleOrDefaultAsync(s => s.AdapterId == Adapter && s.InstanceKey == InstanceKey && s.Name == Name);
        if (row == null) dbContext.Add(new AdapterState { AdapterId = Adapter, InstanceKey = InstanceKey, Name = Name, Value = text, UpdatedOn = DateTime.UtcNow });
        else { row.Value = text; row.UpdatedOn = DateTime.UtcNow; }
        await dbContext.SaveChangesAsync();
    }
}
