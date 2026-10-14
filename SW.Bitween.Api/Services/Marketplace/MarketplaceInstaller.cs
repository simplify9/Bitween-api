extern alias BouncyCastle2;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using SW.Serverless.Sdk;

namespace SW.Bitween.Services.Marketplace;

public record MarketplaceInstall(string AdapterId, string Version, bool Current, string Sha256, string Publisher);

/// <summary>What installing a bundle did, package by package: Installed, Skipped (already here) or Refused, with why.</summary>
public record BundleInstall(string Registry, DateTime? MadeOn, IReadOnlyList<BundleItem> Items);

public record BundleItem(string Id, string Version, string Outcome, string Message, string Sha256);

/// <summary>
/// Installs a version from the registry: checks everything about it first, then publishes it into
/// this Bitween's own storage the way Upload package does. Running it never needs the registry again.
/// </summary>
/// <remarks>
/// Every check fails closed. A registry that can't be reached, a revocation list that doesn't
/// verify, or a key that isn't trusted all refuse the install rather than skip the check.
/// </remarks>
public class MarketplaceInstaller(
    RegistryClient registry,
    BitweenOptions options,
    AdapterCatalog catalog,
    AdapterChanges changes,
    AdapterWorkshop workshop,
    ICloudFilesService cloudFiles,
    ServerlessOptions serverlessOptions,
    RegistryLicences licences = null)
{
    /// <summary>Where the registry a package came from is recorded, in its catalog entry.</summary>
    public const string OriginExtension = "registry";

    public async Task<MarketplaceInstall> InstallAsync(string packageId, string version, bool current, string installedBy)
    {
        var (package, detail, sha256) = await FetchVerifiedAsync(packageId, version, refuseKits: true);
        packageId = detail.PackageId;
        version = detail.Version;
        var published = await workshop.UploadAsync(new MemoryStream(package), version, current, null, $"registry:{detail.Publisher}");
        await RecordOrigin(packageId, detail);
        return new MarketplaceInstall(published.Manifest.Id, published.Version, current, sha256, detail.Publisher);
    }

    /// <summary>
    /// A version's bytes, once every check on it has passed: published, allowed here, not revoked,
    /// and signed by its publisher and countersigned by a trusted registry key. What installing an
    /// adapter and installing a kit both start from.
    /// </summary>
    public async Task<(byte[] Package, RegistryVersion Detail, string Sha256)> FetchVerifiedAsync(string packageId, string version, bool refuseKits)
    {
        packageId = packageId?.Trim().ToLowerInvariant();
        version = version?.Trim();
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            throw new SWValidationException("Package", "Say which package and version to install.");
        if (NativeAdapterDiscoveryService.IsNative(packageId))
            throw new SWValidationException("Package", "Built-in adapters aren't installed from the registry.");

        var trusted = TrustedKeys();
        var detail = await registry.VersionAsync(packageId, version);
        await CheckInstallableAsync(trusted, detail, refuseKits, await registry.RevocationsAsync(), () => EnsureLicensed(packageId));

        var package = await registry.DownloadAsync(packageId, version);
        var sha256 = VerifyBytes(trusted, detail, package);
        return (package, detail, sha256);
    }

    /// <summary>
    /// Installs what a bundle holds: the packages a registry wrote into one zip with both signatures,
    /// its revocation list and the licences, for a Bitween that can't reach it. Each package goes
    /// through every check an online install does, against the keys in Settings, never the bundle's
    /// own; each is installed or refused on its own, and the answer says which. Kits are not
    /// installed from a bundle: they need their parameters.
    /// </summary>
    public async Task<BundleInstall> InstallBundleAsync(Stream zip, bool current, string installedBy)
    {
        var trusted = TrustedKeys();
        using var buffer = new MemoryStream();
        await zip.CopyToAsync(buffer);
        buffer.Position = 0;
        ZipArchive archive;
        try { archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new SWValidationException("BUNDLE_INVALID", "That isn't a zip."); }

        BundleFile bundle;
        using (archive)
        {
            bundle = await ReadJsonAsync<BundleFile>(archive, "bundle.json")
                     ?? throw new SWValidationException("BUNDLE_INVALID", "The zip has no bundle.json, so it isn't a bundle the registry wrote.");
            if (bundle.Packages == null || bundle.Packages.Count == 0)
                throw new SWValidationException("BUNDLE_INVALID", "The bundle names no packages.");
            var revocations = await ReadJsonAsync<RegistrySignedDocument>(archive, "revocations.json")
                              ?? throw new SWValidationException("BUNDLE_INVALID", "The bundle has no revocation list.");

            // Its licences first, so a paid package in it finds its licence; each is checked as a fetched one is.
            if (licences != null && await ReadJsonAsync<List<RegistrySignedDocument>>(archive, "licences.json") is { Count: > 0 } tokens)
                await licences.ImportAsync(tokens);

            var items = new List<BundleItem>();
            foreach (var entry in bundle.Packages)
            {
                var id = entry.Id?.Trim().ToLowerInvariant();
                try
                {
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(entry.Version) || string.IsNullOrWhiteSpace(entry.File))
                        throw new SWValidationException("BUNDLE_INVALID", "A package in the bundle has no id, version or file.");
                    var detail = entry.AsVersion(id);
                    await CheckInstallableAsync(trusted, detail, refuseKits: true, revocations, () => EnsureLicensedOffline(id));
                    var file = archive.GetEntry(entry.File)
                               ?? throw new SWValidationException("BUNDLE_INVALID", $"The bundle names {entry.File} but doesn't hold it.");
                    byte[] package;
                    await using (var stream = file.Open())
                    using (var bytes = new MemoryStream())
                    {
                        await stream.CopyToAsync(bytes);
                        package = bytes.ToArray();
                    }
                    var sha256 = VerifyBytes(trusted, detail, package);
                    var published = await workshop.UploadAsync(new MemoryStream(package), detail.Version, current, null, $"registry:{detail.Publisher}");
                    await RecordOrigin(published.Manifest.Id, detail, bundle.Registry ?? registry.BaseUrl);
                    items.Add(new BundleItem(published.Manifest.Id, published.Version, "Installed", current ? "Installed, now current." : "Installed.", sha256));
                }
                catch (SWValidationException ex) when (ex.Validations.Any(v => v.Key == "REGISTRY_INSTALLED"))
                {
                    items.Add(new BundleItem(id, entry.Version, "Skipped", "Already installed.", entry.Sha256));
                }
                catch (SWException ex)
                {
                    // One refused package leaves the rest to install; nothing of it was published.
                    items.Add(new BundleItem(id, entry.Version, "Refused", ex.Message, entry.Sha256));
                }
            }
            return new BundleInstall(bundle.Registry, bundle.MadeOn, items);
        }
    }

    /// <summary>The keys in Settings, which every signature from a registry or a bundle must verify with.</summary>
    System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> TrustedKeys()
    {
        var (trusted, unreadable) = RegistrySignatures.TrustedKeys(options.RegistryTrustedKeys);
        if (unreadable.Count > 0)
            throw new SWValidationException("REGISTRY_KEYS",
                $"Some registry signing keys in Settings can't be read: {string.Join(", ", unreadable)}.");
        if (trusted.Count == 0)
            throw new SWValidationException("REGISTRY_KEYS",
                "No registry signing keys are set, so nothing from the registry can be verified. Add them under Settings, Marketplace.");
        return trusted;
    }

    /// <summary>
    /// Whether a version may be installed here, before its bytes are looked at: published, allowed
    /// by the Community and licence settings, the right kind, for this Bitween, an id that is the
    /// registry's to fill, and not on the signed revocation list.
    /// </summary>
    async Task CheckInstallableAsync(System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> trusted, RegistryVersion detail,
        bool refuseKits, RegistrySignedDocument revocations, Func<Task> ensureLicensed)
    {
        var packageId = detail.PackageId;
        var version = detail.Version;
        if (!string.Equals(detail.Status, "Published", StringComparison.OrdinalIgnoreCase))
            throw new SWValidationException("REGISTRY_NOT_INSTALLABLE", $"{packageId} {version} is {detail.Status} in the registry and can't be installed.");
        if (string.Equals(detail.Tier, "Community", StringComparison.OrdinalIgnoreCase) && !options.RegistryAllowCommunity)
            throw new SWValidationException("REGISTRY_COMMUNITY",
                "This connector is from a Community publisher. Allow Community connectors under Settings, Marketplace to install it.");
        if (string.Equals(detail.Pricing, "Paid", StringComparison.OrdinalIgnoreCase))
            await ensureLicensed();

        var manifest = AdapterManifest.Parse(detail.Manifest.GetRawText());
        var isKit = IsKit(manifest);
        if (isKit && refuseKits)
            throw new SWValidationException("REGISTRY_KIT",
                $"{packageId} is a kit, not an adapter: install it from its card on the Marketplace tab, which asks for its parameters, or with bitween kit install.");
        if (!isKit && !refuseKits)
            throw new SWValidationException("REGISTRY_NOT_KIT", $"{packageId} is an adapter, not a kit; install it as one.");
        var needs = manifest.Compatibility?.MinVersionOf(AdapterCatalog.Application);
        if (!BitweenInfo.Satisfies(needs, options))
            throw new SWValidationException("ADAPTER_NEEDS_NEWER_BITWEEN",
                $"{packageId} {version} needs Bitween {needs} or later; this is {BitweenInfo.Version(options)}.");

        // A kit isn't stored as an adapter, so it can't take one's place.
        if (!isKit) await EnsureIdIsOurs(packageId, version, IsBitweensOwn(packageId, detail));
        EnsureNotRevoked(trusted, revocations, packageId, version, detail.Sha256);
    }

    /// <summary>The bytes are what was signed, and both signatures verify. Returns the SHA-256.</summary>
    static string VerifyBytes(System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> trusted, RegistryVersion detail, byte[] package)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(package));
        if (!string.Equals(sha256, detail.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new SWValidationException("REGISTRY_SIGNATURE", "The package doesn't match what the registry signed. Nothing was installed.");
        VerifySignatures(trusted, detail, sha256);
        return sha256;
    }

    static async Task<T> ReadJsonAsync<T>(ZipArchive archive, string name) where T : class
    {
        var entry = archive.GetEntry(name);
        if (entry == null) return null;
        await using var stream = entry.Open();
        try { return await JsonSerializer.DeserializeAsync<T>(stream, BundleJson); }
        catch (JsonException ex) { throw new SWValidationException("BUNDLE_INVALID", $"{name} in the bundle can't be read: {ex.Message}"); }
    }

    static readonly JsonSerializerOptions BundleJson = new(JsonSerializerDefaults.Web);

    /// <summary>bundle.json, as the registry writes it.</summary>
    record BundleFile(int Version, string Registry, DateTime? MadeOn, List<BundleEntry> Packages);

    record BundleEntry(string Id, string Version, string File, string Sha256, long Size, string Publisher, string Tier, string Pricing,
        string PublisherKeyId, string PublisherPublicKey, string PublisherSignature, string RegistryKeyId, string RegistrySignature,
        DateTime? SignedOn, JsonElement Manifest)
    {
        /// <summary>The entry as the registry's version endpoint would describe it: what every check reads.</summary>
        public RegistryVersion AsVersion(string id) => new(id, Version?.Trim(), "Published", Sha256, Size, Manifest, Publisher, Tier, Pricing ?? "Free",
            PublisherKeyId, PublisherPublicKey, PublisherSignature, RegistryKeyId, RegistrySignature, SignedOn, null);
    }

    /// <summary>A kit: connectors plus configuration, installed with its parameters rather than as an adapter.</summary>
    public static bool IsKit(AdapterManifest manifest) =>
        manifest?.Kinds?.Any(k => string.Equals(k, "kit", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Bitween's own packages, the data source providers, which Simplify9 publishes under their
    /// historic <c>bitween.</c> ids. An instance installed them by hand from a CLI release before the
    /// marketplace existed, so a registry version may follow one installed that way.
    /// </summary>
    public static bool IsBitweensOwn(string packageId, RegistryVersion detail) =>
        packageId.StartsWith("bitween.", StringComparison.Ordinal)
        && string.Equals(detail.Publisher, "s9", StringComparison.Ordinal)
        && string.Equals(detail.Tier, "Official", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refuses an id this Bitween already has from somewhere other than a registry, so a registry
    /// package can never take the place of an adapter someone published here. Bitween's own
    /// packages are the exception: what was installed by hand was Simplify9's too.
    /// </summary>
    async Task EnsureIdIsOurs(string packageId, string version, bool bitweensOwn)
    {
        var entry = await catalog.GetAsync(packageId);
        if (entry == null)
        {
            // An adapter published before catalogs has a package and no entry.
            var legacy = await cloudFiles.ListAsync(AdapterCatalogPaths.Current(serverlessOptions.AdapterRemotePath, packageId));
            if (legacy.Any() && !bitweensOwn)
                throw new SWValidationException("REGISTRY_ID_TAKEN",
                    $"This Bitween already has an adapter named {packageId} that didn't come from the registry.");
            return;
        }

        if (!bitweensOwn && (entry.Extensions == null || !entry.Extensions.ContainsKey(OriginExtension)))
            throw new SWValidationException("REGISTRY_ID_TAKEN",
                $"This Bitween already has an adapter named {packageId} that didn't come from the registry.");
        if (entry.Find(version) != null)
            throw new SWValidationException("REGISTRY_INSTALLED",
                $"{packageId} {version} is already installed. Make it current from its versions on the Installed tab.");
    }

    static void EnsureNotRevoked(System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> trusted,
        RegistrySignedDocument revocations, string packageId, string version, string sha256)
    {
        if (!RegistrySignatures.VerifyAny(trusted, RegistrySignatures.RevocationsMessage(revocations.Payload), revocations.Signature))
            throw new SWValidationException("REGISTRY_SIGNATURE",
                "The registry's revocation list isn't signed by a trusted key. Nothing was installed.");

        using var document = JsonDocument.Parse(revocations.Payload);
        if (!document.RootElement.TryGetProperty("revoked", out var revoked)) return;
        foreach (var r in revoked.EnumerateArray())
        {
            var sameVersion = r.TryGetProperty("packageId", out var id) && id.GetString() == packageId
                              && r.TryGetProperty("version", out var v) && v.GetString() == version;
            var sameBytes = r.TryGetProperty("sha256", out var sha) && string.Equals(sha.GetString(), sha256, StringComparison.OrdinalIgnoreCase);
            if (sameVersion || sameBytes)
                throw new SWValidationException("REGISTRY_REVOKED", $"{packageId} {version} has been revoked by the registry.");
        }
    }

    static void VerifySignatures(System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> trusted,
        RegistryVersion detail, string sha256)
    {
        if (detail.SignedOn == null)
            throw new SWValidationException("REGISTRY_SIGNATURE", "The registry hasn't countersigned this version.");

        // The registry's signature covers the publisher's key id, so it vouches for that key. The key
        // sent alongside must be the one it names, and must have signed these bytes.
        var countersign = RegistrySignatures.CountersignMessage(detail.PackageId, detail.Version, sha256, detail.Publisher,
            detail.PublisherKeyId, detail.Tier, detail.SignedOn.Value);
        if (!RegistrySignatures.VerifyAny(trusted, countersign, detail.RegistrySignature))
            throw new SWValidationException("REGISTRY_SIGNATURE",
                "The registry's signature on this version doesn't verify with the trusted keys in Settings. Nothing was installed.");

        Ed25519PublicKeyParameters publisherKey;
        try
        {
            publisherKey = RegistrySignatures.ReadPublic(detail.PublisherPublicKey ?? "");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or BouncyCastle2::Org.BouncyCastle.Security.SecurityUtilityException or IOException)
        {
            throw new SWValidationException("REGISTRY_SIGNATURE", "The registry didn't send a readable publisher key. Nothing was installed.");
        }

        if (RegistrySignatures.KeyIdOf(publisherKey) != detail.PublisherKeyId ||
            !RegistrySignatures.Verify(publisherKey, RegistrySignatures.PublishMessage(detail.PackageId, detail.Version, sha256), detail.PublisherSignature))
            throw new SWValidationException("REGISTRY_SIGNATURE", "The publisher's signature on this version doesn't verify. Nothing was installed.");
    }

    /// <summary>
    /// A paid connector installs only where a licence covers it: fetched from the registry now (this
    /// Bitween must be registered, under Settings, Marketplace), checked against the trusted keys.
    /// </summary>
    async Task EnsureLicensed(string packageId)
    {
        if (!registry.Registered)
            throw new SWValidationException("REGISTRY_LICENCE",
                "This is a paid connector. Register this Bitween with the registry under Settings, Marketplace, with the registration code Simplify9 gave, then install it.");
        if (licences == null)
            throw new SWValidationException("REGISTRY_LICENCE", "This is a paid connector and no licence can be checked here.");
        await licences.RefreshAsync();
        var status = await licences.StatusFor(packageId);
        if (status.Allows) return;
        throw new SWValidationException("REGISTRY_LICENCE", status.State switch
        {
            LicenceState.Expired => $"The licence for {packageId} ended on {status.Licence.Ends:yyyy-MM-dd} and its grace is over. Renew it with Simplify9 to install it.",
            LicenceState.NotYet => $"The licence for {packageId} starts on {status.Licence.Starts:yyyy-MM-dd}.",
            _ => $"This Bitween holds no licence for {packageId}. Ask Simplify9 for one; it appears under Settings, Marketplace once issued.",
        });
    }

    /// <summary>From a bundle: the licences it carried were imported; what is held now decides.</summary>
    async Task EnsureLicensedOffline(string packageId)
    {
        if (licences == null)
            throw new SWValidationException("REGISTRY_LICENCE", "This is a paid connector and no licence can be checked here.");
        var status = await licences.StatusFor(packageId);
        if (status.Allows) return;
        throw new SWValidationException("REGISTRY_LICENCE", status.State switch
        {
            LicenceState.Expired => $"The licence for {packageId} ended on {status.Licence.Ends:yyyy-MM-dd} and its grace is over.",
            LicenceState.NotYet => $"The licence for {packageId} starts on {status.Licence.Starts:yyyy-MM-dd}.",
            _ => $"This Bitween holds no licence for {packageId}, and the bundle carried none for it. A bundle made by a registered instance, or by a member of the customer, carries its licences.",
        });
    }

    async Task RecordOrigin(string packageId, RegistryVersion detail, string registryUrl = null)
    {
        var store = new AdapterCatalogStore(cloudFiles, serverlessOptions.AdapterRemotePath);
        var entry = await store.GetAsync(packageId);
        if (entry == null) return;

        entry.Extensions ??= new();
        entry.Extensions[OriginExtension] = JsonSerializer.SerializeToElement(new
        {
            url = registryUrl ?? registry.BaseUrl,
            publisher = detail.Publisher,
            tier = detail.Tier.ToLowerInvariant(),
            pricing = (detail.Pricing ?? "free").ToLowerInvariant(),
        });
        await store.SaveAsync(entry);
        await changes.ForgetEverywhereAsync(packageId);
    }
}
