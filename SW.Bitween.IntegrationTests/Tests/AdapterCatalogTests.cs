using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Adapter manifests, the catalog and version pinning — and, first of all, that a deployment which
/// has none of them behaves exactly as it did: adapters published the old way list, describe and
/// run as before, and subscriptions that pin nothing run the current package.
/// </summary>
[Collection("Bitween")]
public class AdapterCatalogTests(BitweenFixture fixture)
{
    const string SampleProject = "SW.Bitween.SampleHandler";
    const string SampleEntry = "SW.Bitween.SampleHandler.dll";

    static int _seq;
    static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>
    /// A handler published the old way — package and metadata, no manifest, no catalog — or, with
    /// <paramref name="versions"/>, also as versioned packages with a catalog entry naming the last current.
    /// </summary>
    async Task<string> PublishHandler(string[] versions = null, string withdrawn = null,
        string minBitween = null, List<AdapterProperty> properties = null)
    {
        var id = $"infolink6.handlers.catalog{Interlocked.Increment(ref _seq)}";
        await using var scope = fixture.CreateScope();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();

        await AdapterInstaller.InstallAsync(cloudFiles, SampleProject, id, SampleEntry);
        if (versions == null) return id;

        var entry = new AdapterCatalogEntry { Id = id };
        foreach (var version in versions)
        {
            var manifest = new AdapterManifest
            {
                Id = id,
                Version = version,
                DisplayName = $"Sample handler {version}",
                Summary = "Echoes what it is given.",
                Publisher = new AdapterPublisher { Name = "Tests" },
                Tags = ["sample"],
                Kinds = ["handler"],
                Entry = SampleEntry,
                ReleaseNotes = $"Notes for {version}",
                Properties = properties ?? [],
                Compatibility = minBitween == null ? null : new AdapterCompatibility { MinBitweenVersion = minBitween }
            };
            await AdapterInstaller.InstallAsync(cloudFiles, SampleProject, id, SampleEntry, version: version,
                extraFiles: new Dictionary<string, string> { [AdapterManifest.FileName] = manifest.ToJson() });
            entry.Versions.Add(new AdapterVersionRecord
            {
                Version = version,
                PublishedOn = DateTimeOffset.UtcNow,
                PublishedBy = "tests",
                Manifest = manifest,
                Withdrawn = version == withdrawn
            });
            entry.Current = version;
            entry.Manifest = manifest;
        }

        await new AdapterCatalogStore(cloudFiles).SaveAsync(entry);
        scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(id);
        return id;
    }

    async Task<(int DocumentId, int PartnerId)> Groundwork()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Catalog doc"), DocumentFormat.Json);
        var partner = new Partner(Unique("Catalog partner"));
        db.AddRange(document, partner);
        await db.SaveChangesAsync();
        return (document.Id, partner.Id);
    }

    async Task<int> CreateSubscription(string handlerId, string handlerVersion = null)
    {
        var (documentId, partnerId) = await Groundwork();
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);
        var id = (int)await create.Handle(new SubscriptionCreate
        {
            Name = Unique("Catalog subscription"),
            DocumentId = documentId,
            PartnerId = partnerId,
            Type = SubscriptionType.ApiCall,
            HandlerId = handlerId,
            HandlerVersion = handlerVersion
        });

        // Created switched off, as every new subscription is; switched on so it can run.
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Subscription>()
            .Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Inactive, false));
        return id;
    }

    async Task<Subscription> Stored(int id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == id);
    }

    async Task<JArray> CatalogOf(string prefix)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Adapters.Catalog>(scope.ServiceProvider);
        return JArray.FromObject(await handler.Handle(new AdapterSearchRequest { Prefix = prefix }),
            // Camel-cased property names, as the API sends them, with dictionary keys — property
            // names of the adapter — left as they are.
            JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy
                        { ProcessDictionaryKeys = false }
                }
            }));
    }

    async Task<(string HandlerId, XchangeResult Result)> RunOnce(int subscriptionId)
    {
        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile("{\"hello\":1}"));
        }

        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xchange = await db.Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == xchangeId);
            var result = await db.Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
            return (xchange.HandlerId, result);
        }
    }

    // ------------------------------------------------------------------ nothing new published

    [Fact]
    public async Task An_adapter_published_the_old_way_lists_describes_and_runs_as_before()
    {
        var id = await PublishHandler();

        var listed = (await CatalogOf("handlers")).Single(a => (string)a["key"] == id);
        Assert.Null((string)listed["currentVersion"]);
        Assert.Null((string)listed["displayName"]);
        Assert.Empty((JArray)listed["versions"]);
        Assert.NotNull(listed["startupValues"]?["ContentType"]); // described by asking it, as before

        var subscription = await CreateSubscription(id);
        Assert.Null((await Stored(subscription)).HandlerVersion);

        var (handlerId, result) = await RunOnce(subscription);
        Assert.Equal(id, handlerId);
        Assert.True(result.Success, result.Exception);
    }

    [Fact]
    public async Task Catalog_entries_are_never_listed_as_adapters()
    {
        var id = await PublishHandler(versions: ["1.0.0", "1.1.0"]);

        var keys = (await CatalogOf("handlers")).Select(a => (string)a["key"]).ToList();

        Assert.Contains(id, keys);
        Assert.DoesNotContain(keys, k => k.Contains("catalog/") || k.EndsWith(".json"));
        Assert.Equal(1, keys.Count(k => k == id));
    }

    // ------------------------------------------------------------------ the catalog

    [Fact]
    public async Task A_published_adapter_lists_with_its_manifest_and_history()
    {
        var id = await PublishHandler(versions: ["1.0.0", "1.1.0"], withdrawn: "1.0.0");

        var listed = (await CatalogOf("handlers")).Single(a => (string)a["key"] == id);

        Assert.Equal("1.1.0", (string)listed["currentVersion"]);
        Assert.Equal("Sample handler 1.1.0", (string)listed["displayName"]);
        Assert.Equal("Tests", (string)listed["publisher"]);
        var history = (JArray)listed["versionHistory"];
        Assert.Equal(["1.0.0", "1.1.0"], history.Select(v => (string)v["version"]));
        Assert.True((bool)history[0]["withdrawn"]);
        Assert.Equal("Notes for 1.1.0", (string)history[1]["releaseNotes"]);
        // The old field is still there, for the old UI — with what can be pinned, so not the withdrawn one.
        Assert.Equal(["1.1.0"], ((JArray)listed["versions"]).Select(v => (string)v));
    }

    [Fact]
    public async Task A_manifest_that_lists_properties_is_described_from_them()
    {
        var id = await PublishHandler(versions: ["1.0.0"], properties:
        [
            new AdapterProperty { Name = "Endpoint", Required = true, Description = "Where to send it" },
            new AdapterProperty { Name = "Token", Secret = true, Default = "never-shown" }
        ]);

        var values = (await CatalogOf("handlers")).Single(a => (string)a["key"] == id)["startupValues"];

        Assert.NotNull(values?["Endpoint"]);
        Assert.False((bool)values["Endpoint"]!["optional"]);
        Assert.True((bool)values["Token"]!["private"]);
        Assert.Null((string)values["Token"]!["default"]); // a secret's default is withheld, as before
        Assert.Null(values["ContentType"]); // from the manifest, not by asking the adapter
    }

    [Fact]
    public async Task Native_adapters_list_with_their_manifest()
    {
        var smtp = (await CatalogOf("handlers")).Single(a => (string)a["key"] == "NativeSmtpHandler");
        Assert.Equal("Email (SMTP)", (string)smtp["displayName"]);
        Assert.Equal("Simplify9", (string)smtp["publisher"]);
    }

    // ------------------------------------------------------------------ pinning

    [Fact]
    public async Task A_pinned_subscription_runs_its_version_and_the_exchange_remembers_it()
    {
        var id = await PublishHandler(versions: ["1.0.0", "1.1.0"]);

        var subscription = await CreateSubscription(id, "1.0.0");
        Assert.Equal("1.0.0", (await Stored(subscription)).HandlerVersion);

        var (handlerId, result) = await RunOnce(subscription);
        Assert.Equal($"{id}/1.0.0", handlerId);
        Assert.True(result.Success, result.Exception);
    }

    [Fact]
    public async Task An_unpinned_subscription_follows_the_current_version()
    {
        var id = await PublishHandler(versions: ["1.0.0", "1.1.0"]);

        var subscription = await CreateSubscription(id);

        var (handlerId, result) = await RunOnce(subscription);
        Assert.Equal(id, handlerId);
        Assert.True(result.Success, result.Exception);
    }

    [Theory]
    [InlineData("9.9.9")]   // never published
    [InlineData("1.0.0")]   // withdrawn
    public async Task A_version_that_cannot_run_is_refused(string version)
    {
        var id = await PublishHandler(versions: ["1.0.0", "1.1.0"], withdrawn: "1.0.0");

        var error = await Assert.ThrowsAsync<SWValidationException>(() => CreateSubscription(id, version));
        Assert.Contains("ADAPTER_VERSION", error.Message);
    }

    /// <summary>
    /// The request validator checks required settings before the version is checked. It used to
    /// describe the unknown version by starting it, which threw — a 500 instead of the refusal.
    /// </summary>
    [Fact]
    public async Task The_settings_check_leaves_an_unknown_version_to_the_version_check()
    {
        var id = await PublishHandler(versions: ["1.0.0"]);

        await using var scope = fixture.CreateScope();
        var requirements = scope.ServiceProvider.GetRequiredService<AdapterRequirements>();

        Assert.Empty(await requirements.MissingFor($"{id}/9.9.9", []));
    }

    [Fact]
    public async Task A_built_in_adapter_cannot_be_pinned()
    {
        var error = await Assert.ThrowsAsync<SWValidationException>(() =>
            CreateSubscription("NativeHttpHandler", "1.0.0"));
        Assert.Contains("ADAPTER_VERSION", error.Message);
    }

    [Fact]
    public async Task A_version_without_a_catalog_can_be_pinned_when_its_package_exists()
    {
        // Published as versions by an installer that wrote no catalog.
        var id = await PublishHandler();
        await using (var scope = fixture.CreateScope())
            await AdapterInstaller.InstallAsync(scope.ServiceProvider.GetRequiredService<ICloudFilesService>(),
                SampleProject, id, SampleEntry, version: "2.0.0");

        var subscription = await CreateSubscription(id, "2.0.0");

        var (handlerId, result) = await RunOnce(subscription);
        Assert.Equal($"{id}/2.0.0", handlerId);
        Assert.True(result.Success, result.Exception);
    }

    [Fact]
    public async Task A_version_an_older_installer_published_lists_and_can_be_pinned()
    {
        // Where the installer put versions before they had a prefix of their own — beside the
        // current package, under adapters/{id}/. (Only the version, here: a file-system store
        // can't also hold adapters/{id} as a file.)
        var id = $"infolink6.handlers.oldlayout{Interlocked.Increment(ref _seq)}";
        await using (var scope = fixture.CreateScope())
        {
            var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
            await AdapterInstaller.InstallAsync(cloudFiles, SampleProject, $"{id}/1.2.0", SampleEntry);
        }

        var listed = (await CatalogOf("handlers")).Single(a => (string)a["key"] == id);
        Assert.Equal(["1.2.0"], ((JArray)listed["versions"]).Select(v => (string)v));

        var subscription = await CreateSubscription(id, "1.2.0");
        var (handlerId, result) = await RunOnce(subscription);
        Assert.Equal($"{id}/1.2.0", handlerId);
        Assert.True(result.Success, result.Exception);
    }

    [Fact]
    public async Task An_adapter_needing_a_newer_bitween_is_refused_when_chosen_but_not_held_against_a_save()
    {
        var id = await PublishHandler(versions: ["1.0.0"], minBitween: "99.0.0");

        var error = await Assert.ThrowsAsync<SWValidationException>(() => CreateSubscription(id));
        Assert.Contains("ADAPTER_NEEDS_NEWER_BITWEEN", error.Message);
        Assert.Contains("99.0.0", error.Message);
    }
}
