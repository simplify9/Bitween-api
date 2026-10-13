using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SW.Bitween.Cli.Configuration;

/// <summary>One parameter a kit asks for when it is installed.</summary>
public record KitParameter(string Name, string Description, bool Required, bool Secret, string Default, string Example);

/// <summary>A connector a kit needs, at the version its configuration was written for.</summary>
public record KitConnector(string Id, string Version);

/// <summary>
/// A kit: connectors plus the configuration that makes them useful, as one job between two systems.
/// A folder (or a zip of one) holding
///
///   kit.yaml    what it is, the connectors it needs and the parameters it asks for
///   bundle/     bitween apply files, which read the parameters as ${param:NAME}
///   samples/    sample documents, optional
///
/// Installing it installs its connectors from the marketplace, then applies the bundle with the
/// parameters filled in, under the kit's id (or the name given), so --prune and reinstalling work as
/// they do for any configuration.
/// </summary>
public class Kit
{
    public const string FileName = "kit.yaml";
    public const string Kind = "Kit";

    static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{1,99}$", RegexOptions.Compiled);
    static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$", RegexOptions.Compiled);
    static readonly Regex ParameterPattern = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    public string Folder { get; private init; }
    public JsonObject Metadata { get; private init; }
    public string Id => (string)Metadata["id"];
    public string Version => (string)Metadata["version"];
    public string BundleFolder { get; private init; }
    public List<KitConnector> Connectors { get; } = [];
    public List<KitParameter> Parameters { get; } = [];

    /// <summary>The kit in a folder, or in a zip, which is unpacked to a temporary folder first.</summary>
    public static Kit Load(string path)
    {
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var unpacked = Path.Combine(Path.GetTempPath(), "bitween-kit-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(path, unpacked);
            // A built kit carries itself under source/, beside its adapter.json.
            path = File.Exists(Path.Combine(unpacked, "source", FileName)) ? Path.Combine(unpacked, "source") : unpacked;
        }
        if (!Directory.Exists(path)) throw new ConfigException($"There's no kit at {path}: a folder with a {FileName}, or a zip of one.");
        var file = Path.Combine(path, FileName);
        if (!File.Exists(file)) throw new ConfigException($"{path} has no {FileName}.");

        List<JsonNode> documents;
        try { documents = Yaml.Read(File.ReadAllText(file)); }
        catch (YamlDotNet.Core.YamlException ex) { throw new ConfigException($"{file}: {ex.Message}"); }
        if (documents.FirstOrDefault() is not JsonObject root) throw new ConfigException($"{file}: expected a mapping at the top.");

        var problems = new List<string>();
        if ((string)root["apiVersion"] != ConfigFile.ApiVersion) problems.Add($"apiVersion must be {ConfigFile.ApiVersion}.");
        if ((string)root["kind"] != Kind) problems.Add($"kind must be {Kind}.");
        var metadata = root["metadata"] as JsonObject ?? new JsonObject();
        if ((string)metadata["id"] is not { } id || !IdPattern.IsMatch(id))
            problems.Add("metadata.id is the kit's id: lowercase letters, digits, '.', '_' and '-', as s9.salesforce.contacts.");
        if ((string)metadata["version"] is not { } version || !VersionPattern.IsMatch(version))
            problems.Add("metadata.version is a version, as 1.0.0.");
        if (string.IsNullOrWhiteSpace((string)metadata["displayName"])) problems.Add("metadata.displayName is what the kit is called.");
        if (string.IsNullOrWhiteSpace((string)metadata["summary"])) problems.Add("metadata.summary says what it does, in a sentence.");

        var kit = new Kit
        {
            Folder = path,
            Metadata = metadata,
            BundleFolder = Path.GetFullPath(Path.Combine(path, (string)root["bundle"] ?? "bundle")),
        };

        foreach (var item in root["connectors"] as JsonArray ?? [])
        {
            var connectorId = (string)item?["id"];
            var connectorVersion = (string)item?["version"];
            if (string.IsNullOrWhiteSpace(connectorId) || connectorVersion == null || !VersionPattern.IsMatch(connectorVersion))
                problems.Add("connectors: each names a connector and the version the kit was written for, as { id: s9.openapi, version: 1.0.0 }.");
            else kit.Connectors.Add(new KitConnector(connectorId, connectorVersion));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root["parameters"] as JsonArray ?? [])
        {
            var name = (string)item?["name"];
            if (name == null || !ParameterPattern.IsMatch(name)) { problems.Add($"parameters: '{name}' isn't a name: a letter, then letters, digits and '_'."); continue; }
            if (!names.Add(name)) { problems.Add($"parameters: {name} is there twice."); continue; }
            if (string.IsNullOrWhiteSpace((string)item["description"])) problems.Add($"parameters: {name} has no description, which is what the installer reads.");
            kit.Parameters.Add(new KitParameter(name, (string)item["description"], (bool?)item["required"] ?? true, (bool?)item["secret"] ?? false,
                item["default"]?.ToString(), item["example"]?.ToString()));
        }

        if (!Directory.Exists(kit.BundleFolder)) problems.Add($"There's no bundle folder at {kit.BundleFolder}.");
        if (problems.Count > 0) throw new ConfigException($"{file}:\n  - " + string.Join("\n  - ", problems));
        return kit;
    }

    /// <summary>
    /// The values to install with: what was given, then defaults. A required parameter with neither is
    /// left out, so loading the bundle says which are missing.
    /// </summary>
    public Dictionary<string, string> Values(IReadOnlyDictionary<string, string> given)
    {
        // Names match whatever their case: what reaches Bitween's API as JSON has its keys camel-cased.
        given = given.ToDictionary(g => Parameters.FirstOrDefault(p => string.Equals(p.Name, g.Key, StringComparison.OrdinalIgnoreCase))?.Name ?? g.Key,
            g => g.Value, StringComparer.Ordinal);
        var unknown = given.Keys.Where(k => Parameters.All(p => p.Name != k)).ToList();
        if (unknown.Count > 0)
            throw new ConfigException($"The kit has no parameter {string.Join(", ", unknown)}. It has: {string.Join(", ", Parameters.Select(p => p.Name))}.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Parameters)
            if (given.TryGetValue(p.Name, out var value)) values[p.Name] = value;
            else if (p.Default != null) values[p.Name] = p.Default;
            else if (!p.Required) values[p.Name] = "";
        return values;
    }

    public ISet<string> SecretParameters => Parameters.Where(p => p.Secret).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The bundle as apply takes it, named for this installation, with the kit's connectors installed
    /// first. They aren't made current: subscriptions already on another version keep running it, and
    /// the kit's own subscriptions should pin the versions they were written for.
    /// </summary>
    public ConfigFile Configuration(IReadOnlyDictionary<string, string> given, string secretsDir, string name, bool allowMissing = false)
    {
        var config = ConfigFile.Load(BundleFolder, secretsDir, Values(given), SecretParameters, allowMissing, defaultName: name ?? Id,
            contained: true);
        config.Rename(name ?? Id);
        config.Prepend(Kinds.Adapters, Connectors.Select(c => new JsonObject { ["install"] = c.Id, ["version"] = c.Version }));
        return config;
    }

    /// <summary>What's wrong with the kit as written, without a Bitween: its parameters against what the bundle reads.</summary>
    public (List<string> Problems, List<string> Warnings) Check()
    {
        var problems = new List<string>();
        var warnings = new List<string>();

        // Placeholders for every parameter, so the bundle loads without real values.
        var placeholders = Parameters.ToDictionary(p => p.Name, p => p.Example ?? p.Default ?? $"<{p.Name}>", StringComparer.Ordinal);
        ConfigFile config;
        try
        {
            config = ConfigFile.Load(BundleFolder, null, placeholders, SecretParameters, allowMissingParameters: true, defaultName: Id,
                allowMissingSecrets: true, contained: true);
        }
        catch (ConfigException ex)
        {
            problems.Add("The bundle doesn't load: " + ex.Message);
            return (problems, warnings);
        }

        foreach (var used in config.ParametersUsed.Where(u => Parameters.All(p => p.Name != u)))
            problems.Add($"The bundle reads ${{param:{used}}}, which kit.yaml doesn't declare.");
        foreach (var declared in Parameters.Where(p => !config.ParametersUsed.Contains(p.Name)))
            warnings.Add($"Parameter {declared.Name} is declared but nothing in the bundle reads it.");

        // Every adapter the bundle's subscriptions and notifiers run is built in or one of the kit's connectors.
        foreach (var (section, item) in config.Sections.SelectMany(s => s.Value.Select(i => (s.Key, i))))
        foreach (var adapter in AdaptersIn(section, item))
            if (!adapter.StartsWith("native", StringComparison.OrdinalIgnoreCase) && Connectors.All(c => !string.Equals(c.Id, adapter, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{section}: {Kinds.Of(section).KeyOf(item) ?? "an item"} runs {adapter}, which isn't among the kit's connectors.");

        var subscriptions = config.Sections.GetValueOrDefault("subscriptions") ?? [];
        foreach (var s in subscriptions)
        foreach (var block in new[] { "receiver", "handler", "mapper", "validator" })
            if (s[block] is JsonObject b && (string)b["adapter"] is { } a && Connectors.FirstOrDefault(c => string.Equals(c.Id, a, StringComparison.OrdinalIgnoreCase)) is { } c
                && (string)b["version"] != c.Version)
                warnings.Add($"subscription {Context.Quote((string)s["name"])}: its {block} runs {a} unpinned or at another version; pin it to {c.Version} with version: {c.Version}, so a newer one installed later doesn't change it.");

        if (subscriptions.Count == 0) warnings.Add("The bundle has no subscriptions.");
        return (problems, warnings);
    }

    static IEnumerable<string> AdaptersIn(string section, JsonObject item)
    {
        if (section == "subscriptions")
        {
            foreach (var block in new[] { "receiver", "handler", "mapper", "validator" })
                if (item[block] is JsonObject b && (string)b["adapter"] is { Length: > 0 } a) yield return a;
        }
        else if (section is "notifiers" && (string)item["handler"] is { Length: > 0 } h) yield return h;
    }
}
