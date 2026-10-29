using CommandLine;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.Cli;

// ---------------------------------------------------------------------------- configuration

public class ConfigurationOptions
{
    [Option('f', "file", Required = true, HelpText = "The configuration: a .yaml file, or a folder of them.")]
    public string File { get; set; }

    [Option("secrets-dir", HelpText = "A folder with a file per ${secret:NAME}, such as a mounted Kubernetes secret (or BITWEEN_SECRETS_DIR).")]
    public string SecretsDir { get; set; }

    [Option("prune", HelpText = "Also remove what an earlier apply of this configuration made and the file no longer lists.")]
    public bool Prune { get; set; }

    [Option("set", HelpText = "A value for ${param:NAME} as NAME=value; give --set again for each.")]
    public IEnumerable<string> Set { get; set; }

    [Option("values", HelpText = "A .yaml file of NAME: value for ${param:NAME}.")]
    public string ValuesFile { get; set; }

    [Option("profile", HelpText = "Which signed-in Bitween; the current one, or BITWEEN_URL with BITWEEN_TOKEN, unless given.")]
    public string Profile { get; set; }
}

[Verb("plan", HelpText = "Show what apply would change, and change nothing.")]
public class PlanOptions : ConfigurationOptions
{
    [Option("detailed-exitcode", HelpText = "Exit 2 when there are changes to apply, 0 when there are none.")]
    public bool DetailedExitCode { get; set; }
}

[Verb("apply", HelpText = "Make a Bitween's configuration what the file says: create what's missing, update what differs.")]
public class ApplyOptions : ConfigurationOptions;

[Verb("export", HelpText = "Write a Bitween's configuration as a file apply takes.")]
public class ExportOptions
{
    [Option('o', "out", HelpText = "The file to write; standard output unless given.")]
    public string Out { get; set; }

    [Option("name", Default = "exported", HelpText = "The configuration's name, in metadata.name.")]
    public string Name { get; set; }

    [Option("only", Separator = ',', HelpText = "Just these sections, separated by commas: partners,subscriptions.")]
    public IEnumerable<string> Only { get; set; }

    [Option("profile", HelpText = "Which signed-in Bitween; the current one, or BITWEEN_URL with BITWEEN_TOKEN, unless given.")]
    public string Profile { get; set; }
}

public static partial class Commands
{
    /// <summary>Exit code of plan --detailed-exitcode when there's something to apply.</summary>
    public const int Changes = 2;

    public static Task<int> Plan(PlanOptions opts, Profiles profiles) => PlanOrApply(opts, profiles, apply: false, opts.DetailedExitCode);

    public static Task<int> Apply(ApplyOptions opts, Profiles profiles) => PlanOrApply(opts, profiles, apply: true, false);

    static async Task<int> PlanOrApply(ConfigurationOptions opts, Profiles profiles, bool apply, bool detailed)
    {
        ConfigFile config;
        try
        {
            config = ConfigFile.Load(opts.File, opts.SecretsDir ?? Environment.GetEnvironmentVariable("BITWEEN_SECRETS_DIR"),
                Parameters(opts.Set, opts.ValuesFile));
        }
        catch (ConfigException ex)
        {
            Console.WriteLine(ex.Message);
            return Failure;
        }
        return await PlanOrApply(config, profiles, opts.Profile, apply, detailed, opts.Prune);
    }

    /// <summary>Plans a loaded configuration against the signed-in Bitween, and applies it when asked: apply's and a kit's.</summary>
    static async Task<int> PlanOrApply(ConfigFile config, Profiles profiles, string profile, bool apply, bool detailed, bool prune)
    {
        using var api = Api(profiles, profile);
        if (api == null) return Failure;

        var ctx = new Context(api, config.SecretValues);
        var planner = new Planner(ctx, config);
        Console.WriteLine($"Reading {api.Url}...");
        await planner.ReadAsync();
        var plan = await planner.PlanAsync(prune);

        Console.WriteLine($"{(apply ? "Applying" : "Plan for")} {config.Name} on {api.Url}:");
        planner.Print(plan, Console.Out);
        if (plan.Problems.Count > 0) return Failure;
        if (!apply) return detailed && plan.Changes.Count > 0 ? Changes : Success;

        try
        {
            if (plan.Changes.Count > 0) Console.WriteLine("Applying:");
            await planner.ApplyAsync(plan, Console.Out);
        }
        catch (ConfigException ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine("Stopped there; what was done before it stays done. Running apply again carries on.");
            return Failure;
        }
        if (plan.Changes.Count > 0) Console.WriteLine($"Applied {plan.Changes.Count} change(s).");
        return Success;
    }

    public static async Task<int> Export(ExportOptions opts, Profiles profiles)
    {
        var only = opts.Only?.Select(s => s.Trim()).Where(s => s.Length > 0).ToList() ?? [];
        var unknown = only.Where(s => Kinds.All.All(k => k.Section != s)).ToList();
        if (unknown.Count > 0)
        {
            Console.WriteLine($"There's no section {string.Join(", ", unknown)}. Known: {string.Join(", ", Kinds.All.Select(k => k.Section))}.");
            return Failure;
        }

        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;

        var (yaml, secrets, count) = await Exporter.ExportAsync(new Context(api), opts.Name, only);
        if (opts.Out == null) Console.Write(yaml);
        else
        {
            await File.WriteAllTextAsync(opts.Out, yaml);
            Console.WriteLine($"Wrote {count} item(s) from {api.Url} to {opts.Out}.");
        }
        if (secrets.Count > 0)
            Console.Error.WriteLine($"Bitween doesn't show secrets, so the file names them instead. Applying it needs: {string.Join(", ", secrets)}.");
        return Success;
    }
}
