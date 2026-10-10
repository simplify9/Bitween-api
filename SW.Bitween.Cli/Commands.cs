using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using SW.Bitween.Adapters.Tooling;
using SW.Serverless.Contract.Catalog;
using SW.Serverless.Installer;
using SW.Serverless.Tooling;
using SW.Serverless.Tooling.Building;
using SW.Serverless.Tooling.Conformance;
using SW.Serverless.Tooling.Scaffolding;

namespace SW.Bitween.Cli;

// ---------------------------------------------------------------------------- signing in

[Verb("login", HelpText = "Sign in to a Bitween; the other commands then work against it.")]
public class LoginOptions
{
    [Value(0, Required = true, MetaName = "url", HelpText = "The Bitween's address, e.g. https://bitween.example.com")]
    public string Url { get; set; }

    [Option('e', "email", HelpText = "Sign in with this email and a password instead of through the browser.")]
    public string Email { get; set; }

    [Option("password-stdin", HelpText = "Sign in with an email and a password, read from standard input: for scripts and CI.")]
    public bool PasswordStdin { get; set; }

    [Option("no-browser", HelpText = "Sign in through a browser on another machine: show the address, and paste back the code it shows.")]
    public bool NoBrowser { get; set; }

    [Option("profile", HelpText = "A name for this Bitween, to keep several; the address's host unless given.")]
    public string Profile { get; set; }

    [Option("insecure", HelpText = "Don't check its certificate: for a Bitween on a self-signed development certificate only.")]
    public bool Insecure { get; set; }
}

[Verb("logout", HelpText = "Forget a Bitween signed in to: the current one, or --profile.")]
public class LogoutOptions
{
    [Option("profile", HelpText = "Which Bitween to forget.")]
    public string Profile { get; set; }
}

[Verb("whoami", HelpText = "Which Bitween you're signed in to, and as whom.")]
public class WhoAmIOptions
{
    [Option("profile", HelpText = "Ask about this one rather than the current one.")]
    public string Profile { get; set; }
}

// ---------------------------------------------------------------------------- adapters

[Verb("init", HelpText = "Write a new Bitween adapter of one kind, ready to build.")]
public class InitOptions
{
    [Value(0, Required = true, MetaName = "name", HelpText = "Its name, e.g. AcmeOrders: its folder, project and class.")]
    public string Name { get; set; }

    [Option("kind", Default = "handler", HelpText = "handler, mapper, validator or receiver.")]
    public string Kind { get; set; }

    [Option("lang", Default = "dotnet", HelpText = "dotnet, python, node (JavaScript) or typescript.")]
    public string Language { get; set; }

    [Option("id", HelpText = "Its id; from the name when not given (AcmeOrders -> acme.orders).")]
    public string Id { get; set; }

    [Option("dir", Default = ".", HelpText = "The folder to make it in.")]
    public string Directory { get; set; }
}

[Verb("build", HelpText = "Build an adapter into a package, with its manifest and source.")]
public class BuildOptions
{
    [Value(0, MetaName = "project", Default = ".", HelpText = "The adapter's folder, with its adapter.json.")]
    public string Project { get; set; }

    [Option('o', "out", HelpText = "Where the package goes; bin/serverless in the project unless set.")]
    public string Output { get; set; }

    [Option("no-source", HelpText = "Leave the source out of the package.")]
    public bool NoSource { get; set; }

    [Option("allow", HelpText = "Source files whose secret-scan findings are false positives, separated by spaces.")]
    public IEnumerable<string> Allow { get; set; }

    [Option("dry-run", HelpText = "Show the source that would be carried, and build nothing.")]
    public bool DryRun { get; set; }
}

[Verb("test", HelpText = "Start an adapter as Bitween would and check it against the Bitween contract.")]
public class TestOptions
{
    [Value(0, MetaName = "package", Default = ".", HelpText = "A package zip, a package folder, or a project folder (built first).")]
    public string Package { get; set; }

    [Option("settings", HelpText = "A JSON file of settings to run it with: { \"Name\": \"value\" }.")]
    public string Settings { get; set; }

    [Option("allow-delete", HelpText = "Let a receiver's DeleteFile run: it removes or moves a real file at the source.")]
    public bool AllowDelete { get; set; }

    [Option("timeout", Default = 60, HelpText = "Seconds one call may take.")]
    public int Timeout { get; set; }
}

[Verb("run", HelpText = "Start an adapter and call one command.")]
public class RunOptions
{
    [Value(0, MetaName = "package", Default = ".", HelpText = "A package zip, a package folder, or a project folder (built first).")]
    public string Package { get; set; }

    [Option("settings", HelpText = "A JSON file of settings to run it with.")]
    public string Settings { get; set; }

    [Option("call", Required = true, HelpText = "The command, e.g. Handle.")]
    public string Command { get; set; }

    [Option("input", HelpText = "Its argument: JSON, plain text, or @file.")]
    public string Input { get; set; }

    [Option("timeout", Default = 60, HelpText = "Seconds the call may take.")]
    public int Timeout { get; set; }
}

/// <summary>
/// Where publish, promote, versions and withdraw act: the signed-in Bitween, or — given -p or the
/// SWSL_PROVIDER variable — the storage Bitween reads adapters from, as sw-serverless does it.
/// </summary>
public class TargetOptions
{
    [Option("profile", HelpText = "Which signed-in Bitween; the current one unless given.")]
    public string Profile { get; set; }

    [Option('p', "provider", HelpText = "Publish straight to storage instead: s3, as (Azure), oc (Oracle), gc (Google) or local.")]
    public string Provider { get; set; }

    [Option('a', "accesskey", HelpText = "With -p: the storage access key (or SWSL_ACCESS_KEY).")]
    public string AccessKeyId { get; set; }

    [Option('s', "secret", HelpText = "With -p: the storage secret (or SWSL_SECRET_KEY).")]
    public string SecretAccessKey { get; set; }

    [Option('b', "bucketname", HelpText = "With -p: the bucket (or SWSL_BUCKET).")]
    public string BucketName { get; set; }

    [Option('u', "url", HelpText = "With -p: the storage service URL (or SWSL_SERVICE_URL).")]
    public string ServiceUrl { get; set; }

    [Option('c', "cloudfilesconfigpath", HelpText = "With -p: a JSON file with a CloudFiles section.")]
    public string CloudFilesConfigPath { get; set; }

    internal bool ToStorage => !string.IsNullOrWhiteSpace(Provider ?? Environment.GetEnvironmentVariable(StorageResolver.Env.Provider)) && Profile == null;

    internal ServerlessUploadOptions ToUploadOptions() => new StorageFlags
    {
        Provider = Provider, AccessKeyId = AccessKeyId, SecretAccessKey = SecretAccessKey,
        BucketName = BucketName, ServiceUrl = ServiceUrl, CloudFilesConfigPath = CloudFilesConfigPath,
    }.Resolve();
}

[Verb("publish", HelpText = "Publish a package: through the signed-in Bitween, or straight to storage with -p and its flags.")]
public class PublishOptions : TargetOptions
{
    [Value(0, Required = true, MetaName = "package", HelpText = "The package zip bitween adapter build made.")]
    public string Package { get; set; }

    [Option('v', "version", HelpText = "major, minor, patch or an exact version; the package's own version unless given.")]
    public string Version { get; set; }

    [Option("current", HelpText = "Make it the version that runs where nothing pins a version. Not without it.")]
    public bool Current { get; set; }

    [Option("notes", HelpText = "Release notes, instead of the package's own.")]
    public string Notes { get; set; }
}

[Verb("promote", HelpText = "Make a published version the one that runs where nothing pins a version.")]
public class PromoteOptions : TargetOptions
{
    [Value(0, Required = true, MetaName = "adapter-id")] public string AdapterId { get; set; }
    [Value(1, Required = true, MetaName = "version")] public string Version { get; set; }
}

[Verb("versions", HelpText = "List an adapter's published versions.")]
public class VersionsOptions : TargetOptions
{
    [Value(0, Required = true, MetaName = "adapter-id")] public string AdapterId { get; set; }
}

[Verb("withdraw", HelpText = "Take a published version out of use: still listed, never offered for pinning or made current.")]
public class WithdrawOptions : TargetOptions
{
    [Value(0, Required = true, MetaName = "adapter-id")] public string AdapterId { get; set; }
    [Value(1, Required = true, MetaName = "version")] public string Version { get; set; }
}

/// <summary>What each command does.</summary>
public static class Commands
{
    public const int Success = 0;
    public const int Failure = 1;

    // ------------------------------------------------------------------ signing in

    public static async Task<int> Login(LoginOptions opts, Profiles profiles, TextReader input, Func<string> readSecret)
    {
        if (!Uri.TryCreate(opts.Url, UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))
        {
            Console.WriteLine($"'{opts.Url}' isn't an address: give one like https://bitween.example.com");
            return Failure;
        }
        var name = string.IsNullOrWhiteSpace(opts.Profile) ? url.Host : opts.Profile;
        var address = url.GetLeftPart(UriPartial.Path);

        // Through the browser unless an email or a password was given: it works for every account,
        // whichever way it signs in, and the password never passes through the terminal.
        if (string.IsNullOrWhiteSpace(opts.Email) && !opts.PasswordStdin)
        {
            if (url.Scheme == "http" && !url.IsLoopback)
                Console.WriteLine("Warning: signing in over http sends your session unencrypted; use https.");
            var signedIn = await BrowserSignIn.SignInAsync(address, opts.Insecure, openBrowser: !opts.NoBrowser, input);
            profiles.Put(name, signedIn, makeCurrent: true);
            Console.WriteLine($"Signed in to {signedIn.Url} as {signedIn.Email} (profile {name}).");
            return Success;
        }

        if (url.Scheme == "http" && !url.IsLoopback)
            Console.WriteLine("Warning: signing in over http sends your password unencrypted; use https.");

        var email = opts.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Write("Email: ");
            email = input.ReadLine()?.Trim();
        }
        string password;
        if (opts.PasswordStdin) password = input.ReadLine();
        else
        {
            Console.Write("Password: ");
            password = readSecret();
            Console.WriteLine();
        }
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            Console.WriteLine("An email and a password are needed.");
            return Failure;
        }

        var profile = await BitweenApi.SignInAsync(address, email, password, opts.Insecure);
        profiles.Put(name, profile, makeCurrent: true);
        Console.WriteLine($"Signed in to {profile.Url} as {email} (profile {name}).");
        return Success;
    }

    public static async Task<int> Logout(LogoutOptions opts, Profiles profiles)
    {
        var (name, profile) = profiles.Find(opts.Profile);
        if (profile == null)
        {
            Console.WriteLine(name == null ? "You aren't signed in to any Bitween." : $"There's no profile named {name}.");
            return Failure;
        }
        var ended = await BitweenApi.EndSessionAsync(profile);
        profiles.Remove(name);
        Console.WriteLine(ended
            ? $"Signed out of {profile.Url} (profile {name})."
            : $"Forgot {profile.Url} here (profile {name}), but couldn't reach it to end the session there; it ends after 30 days unused.");
        return Success;
    }

    public static async Task<int> WhoAmI(WhoAmIOptions opts, Profiles profiles)
    {
        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;
        var me = await api.GetAsync("api/accounts/profile");
        Console.WriteLine($"{(string)me?["name"]} <{(string)me?["email"]}> on {api.Url}");
        var permissions = me?["permissions"]?.AsArray().Select(p => (string)p).Where(p => p.StartsWith("adapter-source.")).ToList() ?? [];
        Console.WriteLine(permissions.Count == 0
            ? "No adapter permissions: you can build and test, not publish."
            : $"Adapter permissions: {string.Join(", ", permissions)}");
        return Success;
    }

    static BitweenApi Api(Profiles profiles, string profileName)
    {
        var (name, profile) = profiles.Find(profileName);
        if (profile == null)
        {
            Console.WriteLine(name == null || profileName == null
                ? "You aren't signed in to a Bitween: run bitween login <url> first, or give storage flags (-p ...)."
                : $"There's no profile named {name}: bitween login <url> --profile {name}.");
            return null;
        }
        return new BitweenApi(profiles, name, profile);
    }

    // ------------------------------------------------------------------ adapters, locally

    public static int Init(InitOptions opts)
    {
        var result = BitweenAdapters.Scaffold(new ScaffoldRequest
        {
            Name = opts.Name, Id = opts.Id, Language = opts.Language, Kind = opts.Kind, ParentDirectory = opts.Directory,
        });
        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems) Console.WriteLine(problem);
            return Failure;
        }
        Console.WriteLine($"Made a Bitween {opts.Kind} in {result.ProjectDirectory}:");
        foreach (var file in result.Files) Console.WriteLine($"  {file}");
        Console.WriteLine("Next: bitween adapter build, then bitween adapter test --settings settings.json");
        return Success;
    }

    public static async Task<int> Build(BuildOptions opts)
    {
        var request = BitweenAdapters.BuildRequest(opts.Project, opts.Output, Console.WriteLine);
        request.IncludeSource = !opts.NoSource;
        request.AllowedFiles = new HashSet<string>(opts.Allow ?? [], StringComparer.OrdinalIgnoreCase);
        request.DryRun = opts.DryRun;
        var result = await PackageBuilder.BuildAsync(request);

        if (result.SourceFiles.Count > 0)
        {
            Console.WriteLine($"Source carried ({result.SourceFiles.Count} files, {result.SourceFiles.Sum(f => f.Bytes) / 1024} KB):");
            foreach (var (path, bytes) in result.SourceFiles.OrderBy(f => f.Path, StringComparer.Ordinal))
                Console.WriteLine($"  {path} ({bytes} B)");
        }
        foreach (var warning in result.Warnings) Console.WriteLine($"Warning: {warning}");
        foreach (var problem in result.Problems) Console.WriteLine(problem);
        if (!result.Succeeded) return Failure;
        if (!opts.DryRun) Console.WriteLine($"Built {result.ZipPath}");
        return Success;
    }

    public static async Task<int> Test(TestOptions opts)
    {
        var (package, cleanup) = await PackageFolderAsync(opts.Package);
        if (package == null) return Failure;
        try
        {
            var report = await new ConformanceRunner().RunAsync(new ConformanceOptions
            {
                PackageDirectory = package,
                Settings = ReadSettings(opts.Settings),
                AllowDelete = opts.AllowDelete,
                Contracts = { BitweenAdapters.Contract },
                CommandTimeoutSeconds = opts.Timeout,
                Log = Console.WriteLine,
            });
            foreach (var check in report.Checks)
                Console.WriteLine($"{Mark(check.Outcome)} {check.Name}{(string.IsNullOrEmpty(check.Detail) ? "" : " — " + check.Detail)}");
            var failed = report.Checks.Count(c => c.Outcome == CheckOutcome.Failed);
            Console.WriteLine(failed == 0 ? "Conforms." : $"{failed} check{(failed == 1 ? "" : "s")} failed.");
            return report.Passed ? Success : Failure;
        }
        finally
        {
            cleanup();
        }
    }

    public static async Task<int> Run(RunOptions opts)
    {
        var (package, cleanup) = await PackageFolderAsync(opts.Package);
        if (package == null) return Failure;
        try
        {
            await using var host = await LocalAdapterHost.StartAsync(package, ReadSettings(opts.Settings), commandTimeoutSeconds: opts.Timeout);
            Console.WriteLine(await host.CallAsync(opts.Command, ReadInput(opts.Input)));
            return Success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.WriteLine(ex.GetBaseException().Message);
            return Failure;
        }
        finally
        {
            cleanup();
        }
    }

    // ------------------------------------------------------------------ adapters, published

    public static async Task<int> Publish(PublishOptions opts, Profiles profiles)
    {
        if (!File.Exists(opts.Package))
        {
            Console.WriteLine($"There is no package at {opts.Package}; bitween adapter build makes one.");
            return Failure;
        }

        if (opts.ToStorage)
        {
            var result = await PackagePublisher.PublishPackageAsync(CloudFilesFactory.Create(opts.ToUploadOptions()), new PublishPackageRequest
            {
                PackagePath = opts.Package,
                Version = opts.Version,
                Promote = opts.Current,
                ReleaseNotes = opts.Notes,
                PublishedBy = StorageResolver.ResolvePublishedBy(null),
            }, Console.WriteLine);
            Console.WriteLine($"Published {result.Manifest.Id} {result.Version}{(opts.Current ? ", current" : "")}.");
            return Success;
        }

        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(opts.Version)) query.Add("version=" + Uri.EscapeDataString(opts.Version));
        if (opts.Current) query.Add("current=true");
        if (!string.IsNullOrWhiteSpace(opts.Notes)) query.Add("releaseNotes=" + Uri.EscapeDataString(opts.Notes));
        var published = await api.PostFileAsync("api/adapters/packages" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), opts.Package, "application/zip");
        Console.WriteLine($"Published {(string)published?["adapterId"]} {(string)published?["version"]} to {api.Url}" +
                          ((bool?)published?["current"] == true ? ", current." : ". It isn't current: bitween adapter promote makes it so."));
        return Success;
    }

    public static async Task<int> Promote(PromoteOptions opts, Profiles profiles)
    {
        if (opts.ToStorage)
        {
            var work = Path.Combine(Path.GetTempPath(), "bitween-cli", Guid.NewGuid().ToString("N"));
            try { await new AdapterRepository(CloudFilesFactory.Create(opts.ToUploadOptions()), Console.WriteLine).PromoteAsync(opts.AdapterId, opts.Version, work); }
            finally { try { Directory.Delete(work, true); } catch { } }
            return Success;
        }
        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;
        await api.PostAsync("api/adapters/promote", new { adapterId = opts.AdapterId, version = opts.Version });
        Console.WriteLine($"{opts.AdapterId} {opts.Version} is now current on {api.Url}.");
        return Success;
    }

    public static async Task<int> Versions(VersionsOptions opts, Profiles profiles)
    {
        VersionListing listing;
        if (opts.ToStorage)
            listing = await new AdapterRepository(CloudFilesFactory.Create(opts.ToUploadOptions()), _ => { }).ListVersionsAsync(opts.AdapterId);
        else
        {
            using var api = Api(profiles, opts.Profile);
            if (api == null) return Failure;
            var json = await api.GetAsync("api/adapters/versions?adapterId=" + Uri.EscapeDataString(opts.AdapterId));
            listing = json.Deserialize<VersionListing>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (listing.Versions.Count == 0)
        {
            Console.WriteLine($"{opts.AdapterId} has no published versions.");
            return Success;
        }
        foreach (var v in listing.Versions)
            Console.WriteLine($"{(v.Current ? "*" : " ")} {v.Version,-12} {v.PublishedOn?.ToString("yyyy-MM-dd HH:mm") ?? "",-17} {v.PublishedBy,-20}{(v.Withdrawn ? " withdrawn" : "")}");
        return Success;
    }

    public static async Task<int> Withdraw(WithdrawOptions opts, Profiles profiles)
    {
        if (opts.ToStorage)
        {
            await new AdapterRepository(CloudFilesFactory.Create(opts.ToUploadOptions()), Console.WriteLine).WithdrawAsync(opts.AdapterId, opts.Version);
            return Success;
        }
        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;
        await api.PostAsync("api/adapters/withdraw", new { adapterId = opts.AdapterId, version = opts.Version });
        Console.WriteLine($"{opts.AdapterId} {opts.Version} is withdrawn on {api.Url}.");
        return Success;
    }

    // ------------------------------------------------------------------ helpers

    static string Mark(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Passed => "PASS",
        CheckOutcome.Failed => "FAIL",
        _ => "SKIP",
    };

    /// <summary>A package folder from a zip, a package folder, or a project folder built first.</summary>
    static async Task<(string Folder, Action Cleanup)> PackageFolderAsync(string given)
    {
        var path = Path.GetFullPath(given);
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var folder = Path.Combine(Path.GetTempPath(), "bitween-cli", Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(path, folder);
            return (folder, () => TryDelete(folder));
        }

        if (Directory.Exists(path) && File.Exists(Path.Combine(path, AdapterManifest.FileName)) && IsProject(path))
        {
            var output = Path.Combine(Path.GetTempPath(), "bitween-cli", Guid.NewGuid().ToString("N"));
            var built = await PackageBuilder.BuildAsync(BitweenAdapters.BuildRequest(path, output, Console.WriteLine));
            foreach (var problem in built.Problems) Console.WriteLine(problem);
            return built.Succeeded ? (built.PackageDirectory, () => TryDelete(output)) : (null, () => TryDelete(output));
        }

        if (Directory.Exists(path) && File.Exists(Path.Combine(path, AdapterManifest.FileName)))
            return (path, () => { });

        Console.WriteLine($"{given} is neither a package zip, a package folder nor a project folder");
        return (null, () => { });
    }

    /// <summary>A project rather than a built package: a .NET project file, or no sign of a build.</summary>
    static bool IsProject(string folder)
    {
        if (Directory.GetFiles(folder, "*.*proj").Length > 0) return true;
        try
        {
            var runtime = AdapterManifest.Parse(File.ReadAllText(Path.Combine(folder, AdapterManifest.FileName))).Runtime;
            if (string.Equals(runtime, AdapterManifest.PythonRuntime, StringComparison.OrdinalIgnoreCase))
                return !File.Exists(Path.Combine(folder, PythonBuild.EntryScript));
            if (string.Equals(runtime, AdapterManifest.NodeRuntime, StringComparison.OrdinalIgnoreCase))
                return !File.Exists(Path.Combine(folder, "node_modules", "@simplyworks", "sw-serverless", "package.json"));
        }
        catch (JsonException) { }
        return false;
    }

    static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, true); } catch { }
    }

    static IDictionary<string, string> ReadSettings(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new Dictionary<string, string>();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText());
    }

    static object ReadInput(string input)
    {
        if (input == null) return null;
        if (input.StartsWith('@')) input = File.ReadAllText(input[1..]);
        try { return Newtonsoft.Json.Linq.JToken.Parse(input); }
        catch (Newtonsoft.Json.JsonException) { return input; }
    }
}
