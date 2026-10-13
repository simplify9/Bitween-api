using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.Cli;

public class KitOptions
{
    [Value(0, MetaName = "kit", Required = true, HelpText = "The kit: a folder with a kit.yaml, or its zip.")]
    public string Kit { get; set; }

    [Option("set", HelpText = "A parameter as NAME=value; give --set again for each.")]
    public IEnumerable<string> Set { get; set; }

    [Option("values", HelpText = "A .yaml file of NAME: value parameters.")]
    public string ValuesFile { get; set; }

    [Option("secrets-dir", HelpText = "A folder with a file per secret parameter, named after it (or BITWEEN_SECRETS_DIR).")]
    public string SecretsDir { get; set; }

    [Option("profile", HelpText = "Which signed-in Bitween; the current one, or BITWEEN_URL with BITWEEN_TOKEN, unless given.")]
    public string Profile { get; set; }
}

[Verb("test", HelpText = "Check a kit: its kit.yaml, its parameters against what the bundle reads, and the connectors it runs; --plan also shows what it would do on a Bitween.")]
public class KitTestOptions : KitOptions
{
    [Option("plan", HelpText = "Also plan it against the signed-in Bitween, with the parameters given.")]
    public bool Plan { get; set; }
}

[Verb("install", HelpText = "Install a kit: its connectors from the marketplace, then its configuration with the parameters filled in.")]
public class KitInstallOptions : KitOptions
{
    [Option("name", HelpText = "What this installation is called, so one kit can be installed twice; the kit's id unless given.")]
    public string Name { get; set; }

    [Option("dry-run", HelpText = "Show what it would do, and do nothing.")]
    public bool DryRun { get; set; }

    [Option("prune", HelpText = "Also remove what an earlier install of this name made and the kit no longer has.")]
    public bool Prune { get; set; }
}

[Verb("build", HelpText = "Check a kit and pack it into a zip, with an adapter.json the registry reads.")]
public class KitBuildOptions
{
    [Value(0, MetaName = "kit", Required = true, HelpText = "The kit's folder.")]
    public string Kit { get; set; }

    [Option('o', "out", HelpText = "Where to write the zip; the kit's bin folder unless given.")]
    public string Out { get; set; }
}

[Verb("help-kit", Hidden = true)]
public class KitPlaceholderOptions;

public static partial class Commands
{
    /// <summary>Parameters from --values, then --set over them; a secret parameter may also come from the secrets folder.</summary>
    internal static Dictionary<string, string> Parameters(IEnumerable<string> set, string valuesFile, Kit kit = null, string secretsDir = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(valuesFile))
        {
            if (!File.Exists(valuesFile)) throw new ConfigException($"There's no values file at {valuesFile}.");
            if (Yaml.Read(File.ReadAllText(valuesFile)).FirstOrDefault() is not JsonObject map)
                throw new ConfigException($"{valuesFile}: expected NAME: value lines.");
            foreach (var (name, value) in map) values[name] = value?.ToString() ?? "";
        }
        foreach (var pair in set ?? [])
        {
            var at = pair.IndexOf('=');
            if (at <= 0) throw new ConfigException($"--set {pair}: give it as NAME=value.");
            values[pair[..at].Trim()] = pair[(at + 1)..];
        }
        // A secret parameter not given can come from a file of its name, as a mounted Kubernetes secret.
        if (kit != null && secretsDir != null)
            foreach (var p in kit.Parameters.Where(p => p.Secret && !values.ContainsKey(p.Name)))
                if (File.Exists(Path.Combine(secretsDir, p.Name)))
                    values[p.Name] = File.ReadAllText(Path.Combine(secretsDir, p.Name)).TrimEnd('\r', '\n');
        return values;
    }

    public static async Task<int> KitTest(KitTestOptions opts, Profiles profiles)
    {
        Kit kit;
        try { kit = Kit.Load(opts.Kit); }
        catch (ConfigException ex) { Console.WriteLine(ex.Message); return Failure; }

        Console.WriteLine($"{kit.Id} {kit.Version}: {kit.Connectors.Count} connector(s), {kit.Parameters.Count} parameter(s).");
        var (problems, warnings) = kit.Check();
        foreach (var w in warnings) Console.WriteLine("WARN " + w);
        foreach (var p in problems) Console.WriteLine("FAIL " + p);
        if (problems.Count > 0) return Failure;
        Console.WriteLine("PASS kit.yaml, parameters and connectors");
        if (!opts.Plan) return Success;

        ConfigFile config;
        try { config = kit.Configuration(Parameters(opts.Set, opts.ValuesFile, kit, SecretsDirOf(opts.SecretsDir)), SecretsDirOf(opts.SecretsDir), null); }
        catch (ConfigException ex) { Console.WriteLine(ex.Message); return Failure; }
        return await PlanOrApply(config, profiles, opts.Profile, apply: false, detailed: false, prune: false);
    }

    public static async Task<int> KitInstall(KitInstallOptions opts, Profiles profiles)
    {
        ConfigFile config;
        Kit kit;
        try
        {
            kit = Kit.Load(opts.Kit);
            var (problems, _) = kit.Check();
            if (problems.Count > 0) throw new ConfigException("The kit has problems, so it isn't installed:\n  - " + string.Join("\n  - ", problems));
            var secretsDir = SecretsDirOf(opts.SecretsDir);
            config = kit.Configuration(Parameters(opts.Set, opts.ValuesFile, kit, secretsDir), secretsDir, opts.Name);
        }
        catch (ConfigException ex)
        {
            Console.WriteLine(ex.Message);
            return Failure;
        }
        Console.WriteLine($"Installing kit {kit.Id} {kit.Version} as {config.Name}.");
        return await PlanOrApply(config, profiles, opts.Profile, apply: !opts.DryRun, detailed: false, opts.Prune);
    }

    public static Task<int> KitBuild(KitBuildOptions opts)
    {
        Kit kit;
        try
        {
            kit = Kit.Load(opts.Kit);
            var (problems, warnings) = kit.Check();
            foreach (var w in warnings) Console.WriteLine("WARN " + w);
            if (problems.Count > 0) throw new ConfigException("The kit has problems:\n  - " + string.Join("\n  - ", problems));
        }
        catch (ConfigException ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(Failure);
        }

        var out_ = opts.Out ?? Path.Combine(kit.Folder, "bin", $"{kit.Id}-{kit.Version}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(out_))!);
        if (File.Exists(out_)) File.Delete(out_);

        using (var zip = ZipFile.Open(out_, ZipArchiveMode.Create))
        {
            // The kit is its own source: under source/, with each file's hash in the manifest, as an
            // adapter package carries its source, so the registry checks it and a reviewer reads it.
            var hashes = new JsonObject();
            foreach (var file in Directory.EnumerateFiles(kit.Folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(kit.Folder, file).Replace('\\', '/');
                if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.Split('/').Any(p => p.StartsWith('.'))) continue;
                zip.CreateEntryFromFile(file, "source/" + relative);
                using var stream = File.OpenRead(file);
                hashes[relative] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
            }
            // The manifest the registry and the Marketplace read, as an adapter package has, of kind kit.
            var m = kit.Metadata;
            var manifest = new JsonObject
            {
                ["manifestVersion"] = 1,
                ["id"] = kit.Id,
                ["version"] = kit.Version,
                ["displayName"] = (string)m["displayName"],
                ["summary"] = (string)m["summary"],
                ["description"] = (string)m["description"],
                ["publisher"] = m["publisher"]?.DeepClone(),
                ["license"] = (string)m["license"],
                ["categories"] = m["categories"]?.DeepClone(),
                ["tags"] = m["tags"]?.DeepClone(),
                ["kinds"] = new JsonArray("kit"),
                ["source"] = new JsonObject { ["path"] = "source", ["files"] = hashes },
                ["compatibility"] = new JsonObject { ["applications"] = new JsonObject { ["bitween"] = (string)m["bitween"] ?? "10.0.0" } },
                // Unknown to an adapter's manifest, so kept as it is by every tool that reads one.
                ["kit"] = new JsonObject
                {
                    ["connectors"] = new JsonArray(kit.Connectors.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["version"] = c.Version }).ToArray()),
                    ["parameters"] = new JsonArray(kit.Parameters.Select(p => (JsonNode)new JsonObject
                    {
                        ["name"] = p.Name, ["description"] = p.Description, ["required"] = p.Required, ["secret"] = p.Secret,
                        ["default"] = p.Default, ["example"] = p.Example,
                    }).ToArray()),
                },
            };
            using var writer = new StreamWriter(zip.CreateEntry("adapter.json").Open());
            writer.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Built {Path.GetFullPath(out_)}");
        return Task.FromResult(Success);
    }

    static string SecretsDirOf(string given) => given ?? Environment.GetEnvironmentVariable("BITWEEN_SECRETS_DIR");
}
