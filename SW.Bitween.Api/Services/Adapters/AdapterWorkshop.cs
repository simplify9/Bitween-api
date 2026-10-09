using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using SW.Serverless.Tooling;
using SW.Serverless.Tooling.Building;
using SW.Serverless.Tooling.Conformance;
using SW.Serverless.Tooling.Scaffolding;
using SW.Bitween.Adapters.Tooling;

namespace SW.Bitween.Services.Adapters;

public class WorkshopBuild
{
    public bool Succeeded { get; set; }
    public List<string> Problems { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    /// <summary>What the adapter says it is, once built: its settings and commands, for the try panel's form.</summary>
    public AdapterManifest Manifest { get; set; }

    public List<WorkshopCheck> Checks { get; set; } = [];
    public bool Conforms => Succeeded && Checks.All(c => c.Outcome != nameof(CheckOutcome.Failed));
}

public record WorkshopCheck(string Name, string Outcome, string Detail);

public class WorkshopRun
{
    public bool Succeeded { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
    public List<string> Problems { get; set; } = [];
}

/// <summary>
/// The adapter editor's server side: drafts started from a template or a published version, then
/// built, checked, tried and published with SW.Serverless.Tooling — the same code serverless build,
/// test, run and publish are — so a version published here is the one the CLI would have published.
/// </summary>
/// <remarks>
/// <para>
/// Python and JavaScript/TypeScript only, and their own code only: the image has their runtimes but
/// neither pip nor npm, and a dependency would mean fetching packages from the internet in the
/// middle of a save. A draft that names one is refused with the CLI as the way to add it.
/// </para>
/// <para>
/// Building and trying run the draft's code on this server. That is what the permissions are for:
/// <c>adapter-source.edit</c> to do it at all, <c>adapter-source.operate</c> to publish. At most
/// two run at a time, so the editor can't take the server's processing down with it.
/// </para>
/// </remarks>
public class AdapterWorkshop(
    AdapterCatalog catalog,
    AdapterSourceReader sourceReader,
    AdapterStartupValues startupValues,
    ICloudFilesService cloudFiles,
    ServerlessOptions serverlessOptions)
{
    static readonly SemaphoreSlim Running = new(2, 2);
    const int CallTimeoutSeconds = 30;

    // ------------------------------------------------------------------ starting a draft

    /// <summary>A new adapter, from the template serverless init writes.</summary>
    public async Task<AdapterDraft> NewAsync(string name, string language, string kind, string adapterId)
    {
        if (!AdapterDraft.Languages.Contains(language))
            throw new SWValidationException("Language", $"The editor writes {string.Join(", ", AdapterDraft.Languages)} adapters.");

        var id = string.IsNullOrWhiteSpace(adapterId) ? Scaffolder.IdFrom(name ?? "") : adapterId.Trim().ToLowerInvariant();
        if (await catalog.GetAsync(id) != null)
            throw new SWValidationException("AdapterId", $"'{id}' is already published; edit one of its versions instead.");

        var parent = Path.Combine(Path.GetTempPath(), "bitween-workshop", Guid.NewGuid().ToString("N"));
        try
        {
            var made = BitweenAdapters.Scaffold(new ScaffoldRequest { Name = name, Id = id, Language = language, Kind = kind, ParentDirectory = parent });
            if (!made.Succeeded) throw new SWValidationException("Name", string.Join("; ", made.Problems));
            var files = made.Files.ToDictionary(f => f.Replace('\\', '/'), f => File.ReadAllText(Path.Combine(made.ProjectDirectory, f)));
            return new AdapterDraft(id, language, kind, null, files);
        }
        finally
        {
            try { Directory.Delete(parent, true); } catch { }
        }
    }

    /// <summary>A draft of a published Python or Node version, from the source its package carries.</summary>
    public async Task<AdapterDraft> FromVersionAsync(string adapterId, string version)
    {
        var listing = await sourceReader.ListAsync(adapterId, version)
                      ?? throw new SWNotFoundException($"{adapterId} {version}");
        var language = listing.Runtime switch
        {
            AdapterManifest.PythonRuntime => "python",
            AdapterManifest.NodeRuntime => listing.Language == "typescript" ? "typescript" : "node",
            _ => throw new SWValidationException("AdapterId",
                $"Version {version} of '{adapterId}' runs on {listing.Runtime ?? "dotnet"}; the editor writes Python and JavaScript adapters. Build it with the CLI."),
        };
        if (listing.Files.Count == 0)
            throw new SWValidationException("Version", $"Version {version} of '{adapterId}' was published without its source.");

        var files = new Dictionary<string, string>();
        foreach (var file in listing.Files)
        {
            var read = await sourceReader.ReadAsync(adapterId, version, file.Path);
            if (read is { Binary: false }) files[file.Path] = read.Content;
        }

        var manifest = await catalog.ManifestOf(AdapterCatalogPaths.Ref(adapterId, version));
        return new AdapterDraft(adapterId, language, manifest?.Kinds.FirstOrDefault(), version, files);
    }

    // ------------------------------------------------------------------ building, checking, trying

    /// <summary>Builds the draft and, unless only the build is wanted, checks it against its contracts.</summary>
    public async Task<WorkshopBuild> BuildAsync(AdapterDraft draft, IDictionary<string, string> settings, bool check)
    {
        await Running.WaitAsync();
        try
        {
            await using var built = await BuiltAsync(draft);
            var result = built.Result;
            if (!result.Succeeded || !check) return result;

            var report = await new ConformanceRunner().RunAsync(new ConformanceOptions
            {
                PackageDirectory = built.PackageDirectory,
                Settings = settings ?? new Dictionary<string, string>(),
                CommandTimeoutSeconds = CallTimeoutSeconds,
                // A receiver's DeleteFile changes the source it reads; never from here.
                AllowDelete = false,
                Contracts = { BitweenAdapters.Contract },
            });
            result.Checks = report.Checks.Select(c => new WorkshopCheck(c.Name, c.Outcome.ToString(), c.Detail)).ToList();
            return result;
        }
        finally
        {
            Running.Release();
        }
    }

    /// <summary>Builds the draft and calls one command, as a host would, with the settings given.</summary>
    public async Task<WorkshopRun> TryAsync(AdapterDraft draft, IDictionary<string, string> settings, string command, string input)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new SWValidationException("Command", "Say which command to call.");
        await Running.WaitAsync();
        try
        {
            await using var built = await BuiltAsync(draft);
            if (!built.Result.Succeeded) return new WorkshopRun { Problems = built.Result.Problems };
            try
            {
                await using var host = await LocalAdapterHost.StartAsync(built.PackageDirectory, settings ?? new Dictionary<string, string>(),
                    commandTimeoutSeconds: CallTimeoutSeconds);
                return new WorkshopRun { Succeeded = true, Output = await host.CallAsync(command, ReadInput(input)) };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new WorkshopRun { Error = ex.GetBaseException().Message };
            }
        }
        finally
        {
            Running.Release();
        }
    }

    // ------------------------------------------------------------------ publishing

    /// <summary>
    /// Publishes the draft as a new version, not made current: subscriptions keep running what they
    /// run until someone promotes it or pins it. It must build and pass its contract checks first.
    /// </summary>
    public async Task<(string Version, WorkshopBuild Build)> PublishAsync(AdapterDraft draft, string version, string releaseNotes,
        IDictionary<string, string> settings, string publishedBy)
    {
        await Running.WaitAsync();
        try
        {
            await using var built = await BuiltAsync(draft);
            var result = built.Result;
            if (!result.Succeeded) return (null, result);

            var report = await new ConformanceRunner().RunAsync(new ConformanceOptions
            {
                PackageDirectory = built.PackageDirectory,
                Settings = settings ?? new Dictionary<string, string>(),
                CommandTimeoutSeconds = CallTimeoutSeconds,
                Contracts = { BitweenAdapters.Contract },
            });
            result.Checks = report.Checks.Select(c => new WorkshopCheck(c.Name, c.Outcome.ToString(), c.Detail)).ToList();
            if (!report.Passed) return (null, result);

            var published = await PackagePublisher.PublishPackageAsync(cloudFiles, new PublishPackageRequest
            {
                PackagePath = built.ZipPath,
                Version = string.IsNullOrWhiteSpace(version) ? "patch" : version.Trim(),
                Promote = false,
                ReleaseNotes = releaseNotes,
                PublishedBy = publishedBy,
                RemotePath = serverlessOptions.AdapterRemotePath,
            }, _ => { });

            catalog.Forget(draft.AdapterId);
            startupValues.Forget(draft.AdapterId);
            return (published.Version, result);
        }
        finally
        {
            Running.Release();
        }
    }

    /// <summary>Makes a published version the one that runs where nothing is pinned.</summary>
    public async Task PromoteAsync(string adapterId, string version)
    {
        var work = Path.Combine(Path.GetTempPath(), "bitween-workshop", Guid.NewGuid().ToString("N"));
        try
        {
            await new AdapterRepository(cloudFiles, _ => { }, serverlessOptions.AdapterRemotePath).PromoteAsync(adapterId, version, work);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
            catalog.Forget(adapterId);
            startupValues.Forget(adapterId);
        }
    }

    // ------------------------------------------------------------------ packages built elsewhere

    /// <summary>
    /// Publishes a package the bitween CLI built — any language, .NET included — as it would publish
    /// straight to storage, only through Bitween: its manifest is checked, its version settled, and it
    /// is made current only when asked.
    /// </summary>
    public async Task<PublishResult> UploadAsync(Stream zip, string version, bool current, string releaseNotes, string publishedBy)
    {
        var work = Path.Combine(Path.GetTempPath(), "bitween-workshop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var path = Path.Combine(work, "package.zip");
            await using (var file = File.Create(path))
                await zip.CopyToAsync(file);

            PublishResult published;
            try
            {
                published = await PackagePublisher.PublishPackageAsync(cloudFiles, new PublishPackageRequest
                {
                    PackagePath = path,
                    Version = version,
                    Promote = current,
                    ReleaseNotes = releaseNotes,
                    PublishedBy = publishedBy,
                    RemotePath = serverlessOptions.AdapterRemotePath,
                }, _ => { });
            }
            catch (InvalidDataException)
            {
                throw new SWValidationException("Package", "That isn't a zip: send the package bitween adapter build made.");
            }

            catalog.Forget(published.Manifest.Id);
            startupValues.Forget(published.Manifest.Id);
            return published;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>Every version of an adapter, with which is current and which are withdrawn.</summary>
    public Task<VersionListing> VersionsAsync(string adapterId) =>
        new AdapterRepository(cloudFiles, _ => { }, serverlessOptions.AdapterRemotePath).ListVersionsAsync(adapterId);

    /// <summary>Takes a version out of use: still listed, never offered for pinning, never made current.</summary>
    public async Task WithdrawAsync(string adapterId, string version)
    {
        await new AdapterRepository(cloudFiles, _ => { }, serverlessOptions.AdapterRemotePath).WithdrawAsync(adapterId, version);
        catalog.Forget(adapterId);
    }

    // ------------------------------------------------------------------ the build itself

    sealed class Built : IAsyncDisposable
    {
        public string Work { get; init; }
        public WorkshopBuild Result { get; init; }
        public string PackageDirectory { get; init; }
        public string ZipPath { get; init; }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(Work, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    async Task<Built> BuiltAsync(AdapterDraft draft)
    {
        var work = Path.Combine(Path.GetTempPath(), "bitween-workshop", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(work, "project");
        Directory.CreateDirectory(project);

        var files = draft.Files;
        var result = new WorkshopBuild();
        result.Problems.AddRange(Dependencies(files));
        if (result.Problems.Count > 0) return new Built { Work = work, Result = result };

        foreach (var (path, content) in files)
        {
            var target = Path.Combine(project, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content);
        }

        // The draft is for this adapter id whatever its adapter.json says; a typo there would
        // otherwise publish someone else's adapter.
        var manifestPath = Path.Combine(project, AdapterManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            result.Problems.Add($"the draft has no {AdapterManifest.FileName}");
            return new Built { Work = work, Result = result };
        }
        var authored = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject();
        if (authored == null)
        {
            result.Problems.Add($"{AdapterManifest.FileName} isn't a JSON object");
            return new Built { Work = work, Result = result };
        }
        if ((string)authored["id"] != draft.AdapterId)
        {
            result.Problems.Add($"{AdapterManifest.FileName} says \"id\": \"{(string)authored["id"]}\", but this draft is for {draft.AdapterId}");
            return new Built { Work = work, Result = result };
        }

        var build = await PackageBuilder.BuildAsync(BitweenAdapters.BuildRequest(project, Path.Combine(work, "out")));
        result.Succeeded = build.Succeeded;
        result.Problems.AddRange(build.Problems);
        result.Warnings.AddRange(build.Warnings);
        result.Manifest = build.Manifest;
        return new Built { Work = work, Result = result, PackageDirectory = build.PackageDirectory, ZipPath = build.ZipPath };
    }

    static readonly string[] OwnPackages = ["sw-serverless", "sw_serverless", "simplyworks-bitween", "simplyworks_bitween",
        "@simplyworks/sw-serverless", "@simplyworks/bitween"];

    /// <summary>Dependencies beyond the SDKs, which the editor can't fetch: what it says about each.</summary>
    static IEnumerable<string> Dependencies(IDictionary<string, string> files)
    {
        if (files.TryGetValue("requirements.txt", out var requirements))
        {
            var names = requirements.Split('\n').Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => Regex.Match(l, @"^[A-Za-z0-9_.\-]+").Value)
                .Where(n => !OwnPackages.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (names.Count > 0)
                yield return $"requirements.txt names {string.Join(", ", names)}; the editor builds adapters that need only the SDK. " +
                             "Build one with dependencies using serverless build, which vendors them";
        }

        if (files.TryGetValue("package.json", out var packageJson))
        {
            JsonObject document = null;
            try { document = JsonNode.Parse(packageJson)?.AsObject(); } catch (JsonException) { }
            var names = document?["dependencies"]?.AsObject().Select(d => d.Key)
                .Where(n => !OwnPackages.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList() ?? [];
            if (names.Count > 0)
                yield return $"package.json depends on {string.Join(", ", names)}; the editor builds adapters that need only the SDK. " +
                             "Build one with dependencies using serverless build, which installs them";
        }
    }

    /// <summary>The try panel's input, as the CLI's run reads --input: JSON when it parses, the text otherwise.</summary>
    static object ReadInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        try { return Newtonsoft.Json.Linq.JToken.Parse(input); }
        catch (Newtonsoft.Json.JsonException) { return input; }
    }
}
