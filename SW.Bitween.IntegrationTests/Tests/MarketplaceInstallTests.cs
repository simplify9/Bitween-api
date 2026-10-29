extern alias BouncyCastle2;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BouncyCastle2::Org.BouncyCastle.Crypto.Parameters;
using BouncyCastle2::Org.BouncyCastle.Crypto.Signers;
using BouncyCastle2::Org.BouncyCastle.Security;
using BouncyCastle2::Org.BouncyCastle.X509;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Marketplace;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Installing from a registry, against a registry played here: every check that guards an install,
/// and that a good version ends up published in this Bitween's own storage.
/// </summary>
[Collection("Bitween")]
public class MarketplaceInstallTests(BitweenFixture fixture) : IDisposable
{
    static int _seq;

    readonly Ed25519PrivateKeyParameters registryKey = new(new SecureRandom());
    readonly Ed25519PrivateKeyParameters publisherKey = new(new SecureRandom());

    public void Dispose() => RegistryClient.HandlerOverride = null;

    /// <summary>What the registry would serve for one version, with everything a test may spoil.</summary>
    sealed class Listing
    {
        public string Id;
        public string Version = "1.0.0";
        public byte[] Package;
        public string Tier = "Official";
        public string Pricing = "Free";
        public string Status = "Published";
        public string MinBitween = "1.0.0";
        public bool Revoked;
        public byte[] Served;
        public Ed25519PrivateKeyParameters CountersignWith;
    }

    static string PublicText(Ed25519PublicKeyParameters key) =>
        Convert.ToBase64String(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key).GetDerEncoded());

    static string Sign(Ed25519PrivateKeyParameters key, string message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        var bytes = Encoding.UTF8.GetBytes(message);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    static byte[] PythonPackage(string id, string version, string minBitween)
    {
        var manifest = new AdapterManifest
        {
            Id = id, Version = version, Kinds = ["handler"], Runtime = AdapterManifest.PythonRuntime, Entry = "main.py",
            DisplayName = "Test connector", Summary = "For tests.",
            Compatibility = new AdapterCompatibility { Applications = new() { ["bitween"] = minBitween } },
        };
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry(AdapterManifest.FileName).Open())) w.Write(manifest.ToJson());
            using (var w = new StreamWriter(archive.CreateEntry("main.py").Open())) w.Write("print('hello')\n");
        }
        return zip.ToArray();
    }

    Listing NewListing() =>
        new() { Id = $"s9.marketplace{Interlocked.Increment(ref _seq)}{Guid.NewGuid().ToString("N")[..6]}" };

    /// <summary>Answers as the registry's read API would for <paramref name="listing"/>.</summary>
    sealed class FakeRegistry(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    void Serve(Listing l)
    {
        l.Package ??= PythonPackage(l.Id, l.Version, l.MinBitween);
        var sha = Convert.ToHexStringLower(SHA256.HashData(l.Package));
        var publisherPublic = publisherKey.GeneratePublicKey();
        var publisherKeyId = RegistrySignatures.KeyIdOf(publisherPublic);
        var signedOn = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var countersign = RegistrySignatures.CountersignMessage(l.Id, l.Version, sha, "s9", publisherKeyId, l.Tier, signedOn);
        var revocations = JsonSerializer.Serialize(new
        {
            generatedOn = "2026-10-12T10:00:00Z",
            revoked = l.Revoked ? new object[] { new { packageId = l.Id, version = l.Version, sha256 = sha } } : [],
        });

        RegistryClient.HandlerOverride = () => new FakeRegistry(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            object body = path switch
            {
                _ when path.EndsWith("/download") => null,
                "/v1/revocations" => new { payload = revocations, keyId = "test", signature = Sign(registryKey, RegistrySignatures.RevocationsMessage(revocations)) },
                _ when path == $"/v1/packages/{l.Id}/versions/{l.Version}" => new
                {
                    packageId = l.Id, version = l.Version, status = l.Status, sha256 = sha, size = l.Package.Length,
                    manifest = JsonDocument.Parse(AdapterManifest.Parse(Encoding.UTF8.GetString(Read(l.Package, AdapterManifest.FileName))).ToJson()).RootElement,
                    publisher = "s9", tier = l.Tier, pricing = l.Pricing, publisherKeyId,
                    publisherPublicKey = PublicText(publisherPublic),
                    publisherSignature = Sign(publisherKey, RegistrySignatures.PublishMessage(l.Id, l.Version, sha)),
                    registryKeyId = "test",
                    registrySignature = Sign(l.CountersignWith ?? registryKey, countersign),
                    signedOn,
                },
                _ => "not found",
            };
            if (path.EndsWith("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(l.Served ?? l.Package) };
            if (body is string) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        });
    }

    static byte[] Read(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var stream = archive.GetEntry(name)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    BitweenOptions Options(bool allowCommunity = false, bool enabled = true) => new()
    {
        RegistryEnabled = enabled,
        RegistryUrl = "https://registry.test",
        RegistryTrustedKeys = PublicText(registryKey.GeneratePublicKey()),
        RegistryAllowCommunity = allowCommunity,
    };

    async Task<MarketplaceInstall> Install(Listing l, BitweenOptions options = null)
    {
        options ??= Options();
        await using var scope = fixture.CreateScope();
        var installer = ActivatorUtilities.CreateInstance<MarketplaceInstaller>(scope.ServiceProvider,
            new RegistryClient(options), options);
        return await installer.InstallAsync(l.Id, l.Version, current: true, "test");
    }

    async Task<string> Refused(Listing l, BitweenOptions options = null)
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Install(l, options));
        return ex.Validations.First().Key;
    }

    async Task<AdapterCatalogEntry> Installed(string id)
    {
        await using var scope = fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<AdapterCatalog>();
        catalog.Forget(id);
        return await catalog.GetAsync(id);
    }

    [Fact]
    public async Task A_signed_version_is_installed_into_this_Bitweens_storage_and_marked_as_from_the_registry()
    {
        var l = NewListing();
        Serve(l);

        var installed = await Install(l);

        Assert.Equal(l.Id, installed.AdapterId);
        var entry = await Installed(l.Id);
        Assert.Equal("1.0.0", entry.Current);
        Assert.True(entry.Extensions!.ContainsKey(MarketplaceInstaller.OriginExtension));
        Assert.Equal("REGISTRY_INSTALLED", await Refused(l));
    }

    [Fact]
    public async Task A_countersignature_from_an_untrusted_key_installs_nothing()
    {
        var l = NewListing();
        l.CountersignWith = new Ed25519PrivateKeyParameters(new SecureRandom());
        Serve(l);

        Assert.Equal("REGISTRY_SIGNATURE", await Refused(l));
        Assert.Null(await Installed(l.Id));
    }

    [Fact]
    public async Task A_download_that_isnt_what_was_signed_installs_nothing()
    {
        var l = NewListing();
        l.Served = PythonPackage(l.Id, l.Version, "1.0.0").Concat(new byte[] { 0 }).ToArray();
        Serve(l);

        Assert.Equal("REGISTRY_SIGNATURE", await Refused(l));
        Assert.Null(await Installed(l.Id));
    }

    [Fact]
    public async Task A_revoked_version_is_refused()
    {
        var l = NewListing();
        l.Revoked = true;
        Serve(l);
        Assert.Equal("REGISTRY_REVOKED", await Refused(l));
    }

    [Fact]
    public async Task Community_connectors_need_allowing_and_paid_ones_wait_for_licences()
    {
        var community = NewListing();
        community.Tier = "Community";
        Serve(community);
        Assert.Equal("REGISTRY_COMMUNITY", await Refused(community));
        Assert.Equal(community.Id, (await Install(community, Options(allowCommunity: true))).AdapterId);

        var paid = NewListing();
        paid.Pricing = "Paid";
        Serve(paid);
        Assert.Equal("REGISTRY_LICENCE", await Refused(paid));
    }

    [Fact]
    public async Task A_version_needing_a_newer_Bitween_is_refused()
    {
        var l = NewListing();
        l.MinBitween = "999.0.0";
        Serve(l);
        Assert.Equal("ADAPTER_NEEDS_NEWER_BITWEEN", await Refused(l));
    }

    [Fact]
    public async Task An_adapter_published_here_under_the_same_id_is_never_replaced()
    {
        var l = NewListing();
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AdapterWorkshop>()
                .UploadAsync(new MemoryStream(PythonPackage(l.Id, "0.9.0", "1.0.0")), "0.9.0", true, null, "someone");
        Serve(l);

        Assert.Equal("REGISTRY_ID_TAKEN", await Refused(l));
        Assert.Equal("0.9.0", (await Installed(l.Id)).Current);
    }

    /// <summary>
    /// The data source providers were installed by hand from CLI releases before the marketplace;
    /// Simplify9's registry version of one follows it rather than being refused.
    /// </summary>
    [Fact]
    public async Task Bitweens_own_package_installed_by_hand_is_upgraded_from_the_registry()
    {
        var l = NewListing();
        l.Id = $"bitween.db.test{Interlocked.Increment(ref _seq)}";
        l.Version = "2.0.0";
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AdapterWorkshop>()
                .UploadAsync(new MemoryStream(PythonPackage(l.Id, "1.0.0", "1.0.0")), "1.0.0", true, null, "cli release");
        Serve(l);

        Assert.Equal("2.0.0", (await Install(l)).Version);
        Assert.Equal("2.0.0", (await Installed(l.Id)).Current);
    }

    [Fact]
    public async Task Turned_off_nothing_is_fetched()
    {
        var l = NewListing();
        var asked = false;
        RegistryClient.HandlerOverride = () => new FakeRegistry(_ => { asked = true; return new HttpResponseMessage(HttpStatusCode.OK); });

        Assert.Equal("REGISTRY_OFF", await Refused(l, Options(enabled: false)));
        Assert.False(asked);
    }
}
