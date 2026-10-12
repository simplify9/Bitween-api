extern alias BouncyCastle2;

using System;
using System.IO;
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
    ServerlessOptions serverlessOptions)
{
    /// <summary>Where the registry a package came from is recorded, in its catalog entry.</summary>
    public const string OriginExtension = "registry";

    public async Task<MarketplaceInstall> InstallAsync(string packageId, string version, bool current, string installedBy)
    {
        packageId = packageId?.Trim().ToLowerInvariant();
        version = version?.Trim();
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            throw new SWValidationException("Package", "Say which package and version to install.");
        if (NativeAdapterDiscoveryService.IsNative(packageId))
            throw new SWValidationException("Package", "Built-in adapters aren't installed from the registry.");

        var (trusted, unreadable) = RegistrySignatures.TrustedKeys(options.RegistryTrustedKeys);
        if (unreadable.Count > 0)
            throw new SWValidationException("REGISTRY_KEYS",
                $"Some registry signing keys in Settings can't be read: {string.Join(", ", unreadable)}.");
        if (trusted.Count == 0)
            throw new SWValidationException("REGISTRY_KEYS",
                "No registry signing keys are set, so nothing from the registry can be verified. Add them under Settings, Marketplace.");

        var detail = await registry.VersionAsync(packageId, version);
        if (!string.Equals(detail.Status, "Published", StringComparison.OrdinalIgnoreCase))
            throw new SWValidationException("REGISTRY_NOT_INSTALLABLE", $"{packageId} {version} is {detail.Status} in the registry and can't be installed.");
        if (string.Equals(detail.Tier, "Community", StringComparison.OrdinalIgnoreCase) && !options.RegistryAllowCommunity)
            throw new SWValidationException("REGISTRY_COMMUNITY",
                "This connector is from a Community publisher. Allow Community connectors under Settings, Marketplace to install it.");
        if (string.Equals(detail.Pricing, "Paid", StringComparison.OrdinalIgnoreCase))
            throw new SWValidationException("REGISTRY_LICENCE",
                "This is a paid connector. Installing one needs a licence, which this version of Bitween can't check yet.");

        var manifest = AdapterManifest.Parse(detail.Manifest.GetRawText());
        var needs = manifest.Compatibility?.MinVersionOf(AdapterCatalog.Application);
        if (!BitweenInfo.Satisfies(needs, options))
            throw new SWValidationException("ADAPTER_NEEDS_NEWER_BITWEEN",
                $"{packageId} {version} needs Bitween {needs} or later; this is {BitweenInfo.Version(options)}.");

        await EnsureIdIsOurs(packageId, version);
        await EnsureNotRevoked(trusted, packageId, version, detail.Sha256);

        var package = await registry.DownloadAsync(packageId, version);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(package));
        if (!string.Equals(sha256, detail.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new SWValidationException("REGISTRY_SIGNATURE", "The download doesn't match what the registry signed. Nothing was installed.");

        VerifySignatures(trusted, detail, sha256);

        var published = await workshop.UploadAsync(new MemoryStream(package), version, current, null, $"registry:{detail.Publisher}");
        await RecordOrigin(packageId, detail);
        return new MarketplaceInstall(published.Manifest.Id, published.Version, current, sha256, detail.Publisher);
    }

    /// <summary>
    /// Refuses an id this Bitween already has from somewhere other than a registry, so a registry
    /// package can never take the place of an adapter someone published here.
    /// </summary>
    async Task EnsureIdIsOurs(string packageId, string version)
    {
        var entry = await catalog.GetAsync(packageId);
        if (entry == null)
        {
            // An adapter published before catalogs has a package and no entry.
            var legacy = await cloudFiles.ListAsync(AdapterCatalogPaths.Current(serverlessOptions.AdapterRemotePath, packageId));
            if (legacy.Any())
                throw new SWValidationException("REGISTRY_ID_TAKEN",
                    $"This Bitween already has an adapter named {packageId} that didn't come from the registry.");
            return;
        }

        if (entry.Extensions == null || !entry.Extensions.ContainsKey(OriginExtension))
            throw new SWValidationException("REGISTRY_ID_TAKEN",
                $"This Bitween already has an adapter named {packageId} that didn't come from the registry.");
        if (entry.Find(version) != null)
            throw new SWValidationException("REGISTRY_INSTALLED",
                $"{packageId} {version} is already installed. Make it current from its versions on the Installed tab.");
    }

    async Task EnsureNotRevoked(System.Collections.Generic.IReadOnlyList<Ed25519PublicKeyParameters> trusted,
        string packageId, string version, string sha256)
    {
        var revocations = await registry.RevocationsAsync();
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

    async Task RecordOrigin(string packageId, RegistryVersion detail)
    {
        var store = new AdapterCatalogStore(cloudFiles, serverlessOptions.AdapterRemotePath);
        var entry = await store.GetAsync(packageId);
        if (entry == null) return;

        entry.Extensions ??= new();
        entry.Extensions[OriginExtension] = JsonSerializer.SerializeToElement(new
        {
            url = registry.BaseUrl,
            publisher = detail.Publisher,
            tier = detail.Tier.ToLowerInvariant(),
        });
        await store.SaveAsync(entry);
        await changes.ForgetEverywhereAsync(packageId);
    }
}
