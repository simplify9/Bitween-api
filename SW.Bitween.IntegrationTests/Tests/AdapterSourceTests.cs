using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Resources.Adapters;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Reading the source a published version carries: listed from the catalog, read from the package,
/// checked against the manifest's hashes, guarded, and audited — and nothing for a version that
/// carries none, which is every version published before source was.
/// </summary>
[Collection("Bitween")]
public class AdapterSourceTests(BitweenFixture fixture)
{
    static int _seq;

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Publishes each version as serverless build packages it: binaries at the root, source under
    /// source/, the manifest listing each source file's hash. A null source publishes without one.
    /// </summary>
    async Task<string> Publish(params (string Version, Dictionary<string, byte[]> Source, Dictionary<string, string> Recorded)[] versions)
    {
        var id = $"infolink6.handlers.source{Interlocked.Increment(ref _seq)}";
        await using var scope = fixture.CreateScope();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();

        var entry = new AdapterCatalogEntry { Id = id };
        foreach (var (version, source, recorded) in versions)
        {
            var manifest = new AdapterManifest
            {
                Id = id, Version = version, Kinds = ["handler"], Entry = "Adapter.dll",
                Language = "csharp", Runtime = AdapterManifest.DotnetRuntime
            };
            if (source != null)
                manifest.Source = new AdapterSource
                {
                    BuildCommand = "dotnet publish -c Release",
                    Files = recorded ?? source.ToDictionary(s => s.Key, s => Sha(s.Value))
                };

            using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Add(string name, byte[] bytes)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(bytes);
                }

                Add("Adapter.dll", [0x4d, 0x5a, 0, 0]);
                Add(AdapterManifest.FileName, Encoding.UTF8.GetBytes(manifest.ToJson()));
                foreach (var (path, bytes) in source ?? []) Add($"source/{path}", bytes);
            }

            zip.Position = 0;
            await cloudFiles.WriteAsync(zip, new WriteFileSettings
            {
                Key = AdapterCatalogPaths.Version("adapters", id, version),
                ContentType = "application/zip"
            });
            entry.Versions.Add(new AdapterVersionRecord { Version = version, PublishedOn = DateTimeOffset.UtcNow, Manifest = manifest });
            entry.Current = version;
            entry.Manifest = manifest;
        }

        await new AdapterCatalogStore(cloudFiles).SaveAsync(entry);
        scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(id);
        return id;
    }

    static Dictionary<string, byte[]> Files(params (string Path, string Text)[] files) =>
        files.ToDictionary(f => f.Path, f => Encoding.UTF8.GetBytes(f.Text));

    [Fact]
    public async Task Lists_EachVersionsFiles_WithTheirHashes()
    {
        var id = await Publish(
            ("1.0.0", Files(("Handler.cs", "class A {}"), ("Adapter.csproj", "<Project/>")), null),
            ("1.1.0", Files(("Handler.cs", "class A { int x; }"), ("Adapter.csproj", "<Project/>"), ("Util.cs", "static class U {}")), null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Source>(scope.ServiceProvider);

        var v1 = (AdapterSourceListing)await handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0" });
        var v2 = (AdapterSourceListing)await handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.1.0" });

        Assert.Equal(["Adapter.csproj", "Handler.cs"], v1.Files.Select(f => f.Path));
        Assert.Equal(["Adapter.csproj", "Handler.cs", "Util.cs"], v2.Files.Select(f => f.Path));
        Assert.Equal("dotnet publish -c Release", v1.BuildCommand);
        Assert.Equal("csharp", v1.Language);

        // What a diff of the two shows as changed, without reading a file.
        var changed = v2.Files.Where(f => v1.Files.FirstOrDefault(o => o.Path == f.Path)?.Sha256 != f.Sha256).Select(f => f.Path);
        Assert.Equal(["Handler.cs", "Util.cs"], changed);
    }

    [Fact]
    public async Task ReadsAFile_AndAuditsTheRead()
    {
        var id = await Publish(("1.0.0", Files(("src/Handler.cs", "﻿class Handler {}\n")), null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<SourceFile>(scope.ServiceProvider);

        var file = (AdapterSourceFile)await handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0", Path = "src/Handler.cs" });

        Assert.Equal("class Handler {}\n", file.Content);
        Assert.False(file.Binary);

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var access = await db.Set<AdapterSourceAccess>().AsNoTracking().SingleAsync(a => a.AdapterId == id);
        Assert.Equal("1.0.0", access.Version);
        Assert.Equal("src/Handler.cs", access.Path);

        // In the trail, beside every configuration change.
        Assert.True(await db.Set<AuditEntry>().AsNoTracking()
            .AnyAsync(e => e.EntityName == nameof(AdapterSourceAccess) && e.EntityKey == access.Id.ToString()));
    }

    [Fact]
    public async Task ABinaryFile_IsListedButNotShown()
    {
        var id = await Publish(("1.0.0", new Dictionary<string, byte[]> { ["icon.png"] = [0x89, 0x50, 0x4e, 0x47, 0, 1, 2] }, null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var file = (AdapterSourceFile)await ActivatorUtilities.CreateInstance<SourceFile>(scope.ServiceProvider)
            .Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0", Path = "icon.png" });

        Assert.True(file.Binary);
        Assert.Null(file.Content);
        Assert.Equal(7, file.Size);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchItsHash_IsRefused()
    {
        var id = await Publish(("1.0.0", Files(("Handler.cs", "class Tampered {}")),
            new Dictionary<string, string> { ["Handler.cs"] = Sha(Encoding.UTF8.GetBytes("class Original {}")) }));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<SourceFile>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWException>(() =>
            handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0", Path = "Handler.cs" }));
    }

    [Fact]
    public async Task OnlyListedFiles_CanBeRead()
    {
        var id = await Publish(("1.0.0", Files(("Handler.cs", "class A {}")), null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<SourceFile>(scope.ServiceProvider);

        // The package's binaries and manifest are in the zip, but are not source.
        foreach (var path in new[] { "../Adapter.dll", "Adapter.dll", "../adapter.json", "Missing.cs", null })
            await Assert.ThrowsAsync<SWNotFoundException>(() =>
                handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0", Path = path }));
    }

    [Fact]
    public async Task AVersionWithoutSource_ListsNothing()
    {
        var id = await Publish(("1.0.0", null, null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var listing = (AdapterSourceListing)await ActivatorUtilities.CreateInstance<Source>(scope.ServiceProvider)
            .Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0" });

        Assert.Empty(listing.Files);
        Assert.Null(listing.BuildCommand);
    }

    [Fact]
    public async Task AnUnknownVersionOrAdapter_IsNotFound()
    {
        var id = await Publish(("1.0.0", Files(("Handler.cs", "class A {}")), null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Source>(scope.ServiceProvider);

        await Assert.ThrowsAsync<SWNotFoundException>(() => handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "9.9.9" }));
        await Assert.ThrowsAsync<SWNotFoundException>(() => handler.Handle(new AdapterSourceRequest { AdapterId = id, Version = "latest" }));
        await Assert.ThrowsAsync<SWNotFoundException>(() => handler.Handle(new AdapterSourceRequest { AdapterId = "nobody.here", Version = "1.0.0" }));
    }

    [Fact]
    public async Task AnAdapterOnlyInTheCatalog_IsListedUnderItsKinds()
    {
        // Every adapter in another runtime than .NET is published this way: versions and catalog,
        // never adapters/{id}, which older hosts would run with dotnet.
        var id = await Publish(("1.0.0", Files(("main.py", "def handle(x): return x")), null));

        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var listing = ActivatorUtilities.CreateInstance<Catalog>(scope.ServiceProvider);
        var keys = async (string prefix) => ((System.Collections.IEnumerable)await listing.Handle(new Model.AdapterSearchRequest { Prefix = prefix }))
            .Cast<object>().Select(o => (string)o.GetType().GetProperty("Key")!.GetValue(o)!).ToList();

        Assert.Contains(id, await keys("handlers"));
        Assert.DoesNotContain(id, await keys("receivers"));

        // Listed once, however many places it is found.
        Assert.Single(await keys("handlers"), k => k == id);
    }

    [Fact]
    public async Task AnAdapterWhoseVersionsAreAllWithdrawn_IsNotListed()
    {
        var id = await Publish(("1.0.0", Files(("main.py", "x")), null));
        await using var scope = fixture.CreateScope();
        var store = new AdapterCatalogStore(scope.ServiceProvider.GetRequiredService<ICloudFilesService>());
        var entry = await store.GetAsync(id);
        entry.Versions[0].Withdrawn = true;
        await store.SaveAsync(entry);
        scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(id);

        scope.Superuser();
        var rows = (System.Collections.IEnumerable)await ActivatorUtilities.CreateInstance<Catalog>(scope.ServiceProvider)
            .Handle(new Model.AdapterSearchRequest { Prefix = "handlers" });
        Assert.DoesNotContain(rows.Cast<object>(), o => (string)o.GetType().GetProperty("Key")!.GetValue(o)! == id);
    }

    [Fact]
    public async Task AViewer_CannotReadSource()
    {
        var id = await Publish(("1.0.0", Files(("Handler.cs", "class A {}")), null));

        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer($"source-viewer-{Interlocked.Increment(ref _seq)}");

        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<Source>(scope.ServiceProvider)
            .Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0" }));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<SourceFile>(scope.ServiceProvider)
            .Handle(new AdapterSourceRequest { AdapterId = id, Version = "1.0.0", Path = "Handler.cs" }));

        Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<AdapterSourceAccess>().AnyAsync(a => a.AdapterId == id));
    }
}
