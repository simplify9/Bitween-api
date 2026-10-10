using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Resources.AdapterDrafts;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The adapter editor, end to end on real storage: a Python or JavaScript adapter started from the
/// template, built and checked against the Bitween contract, tried, published as a version that is
/// not current, made current, and started again from what was published — with what it refuses,
/// who may do it, and the audit trail.
/// </summary>
[Collection("Bitween")]
public class AdapterEditorTests(BitweenFixture fixture)
{
    static int _seq;
    static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _seq)}{Guid.NewGuid().ToString("N")[..6]}";

    static readonly Dictionary<string, string> Settings = new() { ["ApiKey"] = "k" };

    async Task<T> As<T>(Func<IServiceProvider, Task<T>> act)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return await act(scope.ServiceProvider);
    }

    Task<int> NewDraft(string name, string language = "python", string kind = "handler") =>
        As(async sp => (int)await ActivatorUtilities.CreateInstance<Create>(sp)
            .Handle(new AdapterDraftCreate { Name = name, Language = language, Kind = kind }));

    Task<AdapterDraft> Draft(int id) =>
        As(sp => sp.GetRequiredService<BitweenDbContext>().Set<AdapterDraft>().AsNoTracking().SingleAsync(d => d.Id == id));

    Task<JObject> Build(int id, Dictionary<string, string> settings = null) =>
        As(async sp => JObject.FromObject(await ActivatorUtilities.CreateInstance<Build>(sp)
            .Handle(id, new AdapterDraftRun { Settings = settings ?? Settings })));

    async Task Save(int id, Func<Dictionary<string, string>, Dictionary<string, string>> change)
    {
        var files = change((await Draft(id)).Files);
        await As(sp => ActivatorUtilities.CreateInstance<Update>(sp).Handle(id, new AdapterDraftUpdate { Files = files }));
    }

    [Theory]
    [InlineData("python")]
    [InlineData("node")]
    public async Task A_new_adapter_builds_conforms_and_can_be_tried(string language)
    {
        var name = Unique(language == "python" ? "PyEdit" : "JsEdit");
        var id = await NewDraft(name, language);
        var draft = await Draft(id);
        Assert.Equal(language, draft.Language);
        Assert.Contains(language == "python" ? "main.py" : "main.js", draft.Files.Keys);

        var built = await Build(id);
        Assert.True((bool)built["Succeeded"], built.ToString());
        Assert.True((bool)built["Conforms"], built.ToString());
        Assert.Contains(built["Settings"]!, s => (string)s["Name"] == "ApiKey" && (bool)s["Secret"]);
        Assert.Equal(["Handle"], built["Commands"]!.Select(c => (string)c).ToArray());

        var tried = await As(sp => ActivatorUtilities.CreateInstance<Try>(sp).Handle(id, new AdapterDraftRun
        {
            Settings = Settings, Command = "Handle", Input = "{\"Data\":\"hello\",\"Filename\":\"a.txt\"}",
        }));
        var run = (WorkshopRun)tried;
        Assert.True(run.Succeeded, run.Error ?? string.Join("; ", run.Problems));
        Assert.Equal("hello", (string)JObject.Parse(run.Output)["Data"]);
    }

    [Fact]
    public async Task An_edit_that_breaks_the_adapter_says_why()
    {
        var id = await NewDraft(Unique("PyBroken"));
        await Save(id, files => { files["main.py"] = files["main.py"].Replace("def handle(", "def handle(self,"); return files; });

        var built = await Build(id);
        Assert.False((bool)built["Succeeded"]);
        Assert.Contains("SyntaxError", string.Join(" ", built["Problems"]!.Select(p => (string)p)));
    }

    [Fact]
    public async Task What_the_editor_cannot_build_is_refused_with_the_reason()
    {
        var id = await NewDraft(Unique("PyDeps"));
        await Save(id, files => { files["requirements.txt"] += "\nrequests==2.32.3\n"; return files; });
        var built = await Build(id);
        Assert.False((bool)built["Succeeded"]);
        Assert.Contains("requests", (string)built["Problems"]![0]);

        var other = await NewDraft(Unique("PyRenamed"));
        await Save(other, files => { files["adapter.json"] = files["adapter.json"].Replace("\"id\": \"", "\"id\": \"someone.else."); return files; });
        Assert.Contains("this draft is for", (string)(await Build(other))["Problems"]![0]);

        foreach (var path in new[] { "../escape.py", "/abs.py", "a\\\\b.py", "a/../b.py" })
            await Assert.ThrowsAsync<SWValidationException>(() => Save(other, files => { files[path] = "x"; return files; }));
    }

    [Fact]
    public async Task A_save_over_someone_else_s_is_refused_unless_it_says_to()
    {
        var id = await NewDraft(Unique("PyTwo"));
        var opened = await Draft(id);

        // Someone else saves first, from the same version.
        await As(sp => ActivatorUtilities.CreateInstance<Update>(sp).Handle(id, new AdapterDraftUpdate
        {
            Files = opened.Files.ToDictionary(f => f.Key, f => f.Value + (f.Key == "main.py" ? "\n# theirs\n" : "")),
            BaseHash = opened.FilesHash,
        }));

        var mine = opened.Files.ToDictionary(f => f.Key, f => f.Value + (f.Key == "main.py" ? "\n# mine\n" : ""));
        var refused = await Assert.ThrowsAsync<SWValidationException>(() =>
            As(sp => ActivatorUtilities.CreateInstance<Update>(sp).Handle(id, new AdapterDraftUpdate { Files = mine, BaseHash = opened.FilesHash })));
        Assert.Contains("after you opened it", refused.Message + string.Join(" ", refused.Validations.Select(v => v.Value)));
        Assert.Contains("# theirs", (await Draft(id)).Files["main.py"]);

        // Saving over theirs, knowingly: without a base, as a client from before this does.
        await As(sp => ActivatorUtilities.CreateInstance<Update>(sp).Handle(id, new AdapterDraftUpdate { Files = mine }));
        Assert.Contains("# mine", (await Draft(id)).Files["main.py"]);
    }

    [Fact]
    public async Task A_try_that_uses_too_much_memory_is_stopped_and_says_so()
    {
        var id = await NewDraft(Unique("PyHog"));
        await Save(id, files =>
        {
            // Holds on to more and more, as a leak would, until something stops it.
            files["main.py"] = files["main.py"].Replace("import sw_serverless as sw",
                "import time\nimport sw_serverless as sw\n_held = []\n\ndef _hog():\n    for _ in range(120):\n        _held.append(bytearray(8 * 1024 * 1024))\n        time.sleep(0.05)\n");
            files["main.py"] = System.Text.RegularExpressions.Regex.Replace(files["main.py"],
                @"(def handle\([^)]*\)[^:]*:\n)", "$1        _hog()\n");
            return files;
        });

        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        var memory = options.AdapterEditorMemoryMb;
        options.AdapterEditorMemoryMb = 150;
        try
        {
            var run = (WorkshopRun)await As(sp => ActivatorUtilities.CreateInstance<Try>(sp).Handle(id, new AdapterDraftRun
            {
                Settings = Settings, Command = "Handle", Input = "{\"Data\":\"x\",\"Filename\":\"a.txt\"}",
            }));
            Assert.False(run.Succeeded);
            Assert.Contains("memory limit of 150 MB", run.Error ?? string.Join("; ", run.Problems));
        }
        finally
        {
            options.AdapterEditorMemoryMb = memory;
        }
    }

    async Task<T> WithDependencies<T>(Func<Task<T>> act)
    {
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        options.AdapterEditorDependencies = true;
        try { return await act(); }
        finally { options.AdapterEditorDependencies = false; }
    }

    [Fact]
    public async Task With_dependencies_on_only_plain_names_and_versions_are_fetched()
    {
        var py = await NewDraft(Unique("PySources"));
        await Save(py, files =>
        {
            files["requirements.txt"] += "\n--index-url https://example.com/simple\nhttps://example.com/x-1.0-py3-none-any.whl\n" +
                                         "./local\nthing @ git+https://example.com/thing.git\n";
            return files;
        });
        var problems = (await WithDependencies(() => Build(py)))["Problems"]!.Select(p => (string)p).ToList();
        Assert.Equal(4, problems.Count(p => p.Contains("isn't a package name and version")));

        var js = await NewDraft(Unique("JsSources"), "node");
        await Save(js, files =>
        {
            var package = System.Text.Json.Nodes.JsonNode.Parse(files["package.json"])!.AsObject();
            package["dependencies"] = new System.Text.Json.Nodes.JsonObject
            {
                ["local"] = "file:../../etc", ["remote"] = "git+https://example.com/r.git", ["alias"] = "npm:left-pad@1.3.0",
                ["fine"] = "^7.0.0",
            };
            files["package.json"] = package.ToJsonString();
            return files;
        });
        problems = (await WithDependencies(() => Build(js)))["Problems"]!.Select(p => (string)p).ToList();
        Assert.Equal(3, problems.Count(p => p.Contains("isn't a package and version range")));
        Assert.DoesNotContain(problems, p => p.Contains("\"fine\""));
    }

    [SkippableFact]
    public async Task With_dependencies_on_a_package_from_the_index_is_vendored_and_runs()
    {
        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            await probe.ConnectAsync("pypi.org", 443).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Skip.If(true, $"PyPI can't be reached from here: {ex.GetBaseException().Message}");
        }

        var id = await NewDraft(Unique("PySix"));
        await Save(id, files =>
        {
            files["requirements.txt"] += "\nsix==1.16.0\n";
            files["main.py"] = "import six\n" + files["main.py"];
            return files;
        });

        Assert.False((bool)(await Build(id))["Succeeded"]);
        var built = await WithDependencies(() => Build(id));
        Assert.True((bool)built["Succeeded"], built.ToString());
        Assert.True((bool)built["Conforms"], built.ToString());
    }

    [Fact]
    public async Task Publishing_makes_a_version_that_is_not_current_until_it_is_promoted()
    {
        var name = Unique("PyPublish");
        var id = await NewDraft(name);
        var adapterId = (await Draft(id)).AdapterId;

        var published = JObject.FromObject(await As(sp => ActivatorUtilities.CreateInstance<Publish>(sp)
            .Handle(id, new AdapterDraftPublish { Version = "1.0.0", ReleaseNotes = "First", Settings = Settings })));
        Assert.True((bool)published["Published"], published.ToString());
        Assert.Equal("1.0.0", (string)published["Version"]);

        var entry = await As(sp => sp.GetRequiredService<AdapterCatalog>().GetAsync(adapterId));
        Assert.Null(entry.Current);
        var version = entry.Find("1.0.0");
        Assert.Equal("First", version.Manifest.ReleaseNotes);
        Assert.Equal("python", version.Manifest.Runtime);
        Assert.Contains("main.py", version.Manifest.Source.Files.Keys);
        Assert.Equal("1.0.0", (await Draft(id)).BaseVersion);

        // A version can't be published twice; the next one is a patch on top.
        var again = JObject.FromObject(await As(sp => ActivatorUtilities.CreateInstance<Publish>(sp)
            .Handle(id, new AdapterDraftPublish { Settings = Settings })));
        Assert.Equal("1.0.1", (string)again["Version"]);

        await As(sp => ActivatorUtilities.CreateInstance<Resources.Adapters.Promote>(sp)
            .Handle(new Resources.Adapters.AdapterPromoteRequest { AdapterId = adapterId, Version = "1.0.0" }));
        Assert.Equal("1.0.0", (await As(sp => sp.GetRequiredService<AdapterCatalog>().GetAsync(adapterId))).Current);

        var releases = await As(sp => sp.GetRequiredService<BitweenDbContext>().Set<AdapterRelease>().AsNoTracking()
            .Where(r => r.AdapterId == adapterId).OrderBy(r => r.Id).Select(r => r.Action + " " + r.Version).ToListAsync());
        Assert.Equal(["published 1.0.0", "published 1.0.1", "promoted 1.0.0"], releases);

        var audited = await As(sp => sp.GetRequiredService<BitweenDbContext>().Set<AuditEntry>().AsNoTracking()
            .Where(e => e.EntityName == nameof(AdapterDraft) && e.EntityKey == id.ToString()).ToListAsync());
        Assert.NotEmpty(audited);
        Assert.DoesNotContain(audited, e => (e.Changes ?? "").Contains("def handle"));
    }

    [Fact]
    public async Task A_draft_can_start_from_a_published_version_s_source()
    {
        var id = await NewDraft(Unique("PyAgain"));
        await Save(id, files => { files["main.py"] = files["main.py"].Replace("Send file.data", "Send the file's data"); return files; });
        var original = await Draft(id);
        await As(sp => ActivatorUtilities.CreateInstance<Publish>(sp)
            .Handle(id, new AdapterDraftPublish { Version = "2.0.0", Settings = Settings }));

        var copy = await As(async sp => (int)await ActivatorUtilities.CreateInstance<Create>(sp)
            .Handle(new AdapterDraftCreate { FromAdapterId = original.AdapterId, FromVersion = "2.0.0" }));
        var started = await Draft(copy);

        Assert.Equal("python", started.Language);
        Assert.Equal("handler", started.Kind);
        Assert.Equal("2.0.0", started.BaseVersion);
        Assert.Equal(original.Files["main.py"], started.Files["main.py"]);
    }

    [Fact]
    public async Task A_name_already_published_is_edited_from_a_version_not_started_again()
    {
        var name = Unique("PyTaken");
        var id = await NewDraft(name);
        await As(sp => ActivatorUtilities.CreateInstance<Publish>(sp).Handle(id, new AdapterDraftPublish { Settings = Settings }));

        await Assert.ThrowsAsync<SWValidationException>(() => NewDraft(name));
    }

    [Fact]
    public async Task Without_the_permission_nothing_is_built_published_or_promoted()
    {
        var id = await NewDraft(Unique("PyGuarded"));

        await using var scope = fixture.CreateScope();
        await scope.AsNewViewer($"editor-viewer-{Guid.NewGuid():N}");
        var sp = scope.ServiceProvider;

        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<Get>(sp).Handle(id));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<Build>(sp).Handle(id, new AdapterDraftRun()));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<Publish>(sp).Handle(id, new AdapterDraftPublish()));
        await Assert.ThrowsAsync<SWUnauthorizedException>(() => ActivatorUtilities.CreateInstance<Resources.Adapters.Promote>(sp)
            .Handle(new Resources.Adapters.AdapterPromoteRequest { AdapterId = "x", Version = "1.0.0" }));
    }
}
