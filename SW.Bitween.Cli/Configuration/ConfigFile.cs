using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SW.Bitween.Cli.Configuration;

/// <summary>A configuration problem: in the file, or between the file and the Bitween.</summary>
public class ConfigException(string message) : Exception(message);

/// <summary>
/// The desired configuration: one file, or every .yaml and .yml in a folder, merged. Each document is
///
///   apiVersion: bitween/v1
///   kind: Configuration
///   metadata: { name: acme }
///   informationTypes: [...]
///   partners: [...]
///
/// Strings may hold ${secret:NAME} (from the variable NAME, or the file NAME in the secrets folder),
/// ${env:NAME} and ${file:relative/path}; secrets are never printed.
/// </summary>
public class ConfigFile
{
    public const string ApiVersion = "bitween/v1";
    public const string Kind = "Configuration";

    /// <summary>What this configuration is called: what it remembers it manages is kept under this name.</summary>
    public string Name { get; private set; }

    public Dictionary<string, List<JsonObject>> Sections { get; } = new(StringComparer.Ordinal);

    /// <summary>Every value that came from a secret, so it can be kept out of what's printed.</summary>
    public HashSet<string> SecretValues { get; } = new(StringComparer.Ordinal);

    static readonly Regex Reference = new(@"\$\{(secret|env|file):([^}]+)\}", RegexOptions.Compiled);

    public static ConfigFile Load(string path, string secretsDir = null)
    {
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal).ToList()
            : File.Exists(path) ? [path] : throw new ConfigException($"There's no file or folder at {path}.");
        if (files.Count == 0) throw new ConfigException($"There are no .yaml files in {path}.");

        var config = new ConfigFile();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            List<JsonNode> documents;
            try { documents = Yaml.Read(File.ReadAllText(file)); }
            catch (YamlDotNet.Core.YamlException ex) { throw new ConfigException($"{file}: {ex.Message}"); }

            foreach (var document in documents.Where(d => d != null))
            {
                if (document is not JsonObject root) throw new ConfigException($"{file}: expected a mapping at the top.");
                config.Add(file, root, Path.GetDirectoryName(Path.GetFullPath(file)), secretsDir, missing);
            }
        }

        if (missing.Count > 0)
            throw new ConfigException("These aren't set: " + string.Join(", ", missing) +
                                      ". Set each as a variable, or put it in a file of that name in the secrets folder (--secrets-dir).");
        if (config.Name == null) throw new ConfigException("No metadata.name: name the configuration, e.g. metadata: { name: acme }.");
        return config;
    }

    void Add(string file, JsonObject root, string folder, string secretsDir, ISet<string> missing)
    {
        var version = (string)root["apiVersion"];
        var kind = (string)root["kind"];
        if (version != ApiVersion) throw new ConfigException($"{file}: apiVersion must be {ApiVersion}, not {version ?? "missing"}.");
        if (kind != Kind) throw new ConfigException($"{file}: kind must be {Kind}, not {kind ?? "missing"}.");

        var name = (string)root["metadata"]?["name"];
        if (name != null)
        {
            if (Name != null && Name != name) throw new ConfigException($"{file}: named {name}, but another file is named {Name}. One configuration, one name.");
            Name = name;
        }

        foreach (var (section, value) in root)
        {
            if (section is "apiVersion" or "kind" or "metadata") continue;
            if (!Kinds.Sections.Contains(section))
                throw new ConfigException($"{file}: there's no section {section}. Known: {string.Join(", ", Kinds.Sections)}.");
            if (value is null) continue;
            if (value is not JsonArray items) throw new ConfigException($"{file}: {section} must be a list.");

            var list = Sections.TryGetValue(section, out var existing) ? existing : Sections[section] = [];
            foreach (var item in items)
            {
                if (item is not JsonObject obj) throw new ConfigException($"{file}: every item in {section} must be a mapping.");
                var resolved = (JsonObject)Resolve(obj.DeepClone(), folder, secretsDir, missing);
                // A package is found next to the file that names it, wherever apply runs from.
                if (section == Kinds.Adapters && (string)resolved["package"] is { Length: > 0 } package)
                    resolved["package"] = Path.GetFullPath(Path.Combine(folder, package));
                list.Add(resolved);
            }
        }
    }

    JsonNode Resolve(JsonNode node, string folder, string secretsDir, ISet<string> missing)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList()) obj[key] = Resolve(obj[key]?.DeepClone(), folder, secretsDir, missing);
                return obj;
            case JsonArray list:
                return new JsonArray(list.Select(i => Resolve(i?.DeepClone(), folder, secretsDir, missing)).ToArray());
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Contains("${"):
                var secret = false;
                var resolved = Reference.Replace(s, m =>
                {
                    var (source, name) = (m.Groups[1].Value, m.Groups[2].Value.Trim());
                    switch (source)
                    {
                        case "secret":
                            secret = true;
                            var value = Environment.GetEnvironmentVariable(name);
                            var inFolder = secretsDir == null ? null : Path.Combine(secretsDir, name);
                            if (value == null && inFolder != null && File.Exists(inFolder)) value = File.ReadAllText(inFolder).TrimEnd('\r', '\n');
                            if (value == null) missing.Add(name);
                            return value ?? "";
                        case "env":
                            var env = Environment.GetEnvironmentVariable(name);
                            if (env == null) missing.Add(name);
                            return env ?? "";
                        default:
                            var full = Path.GetFullPath(Path.Combine(folder, name));
                            if (!File.Exists(full)) throw new ConfigException($"${{file:{name}}}: there's no file at {full}.");
                            return File.ReadAllText(full);
                    }
                });
                if (secret && resolved.Length > 0) SecretValues.Add(resolved);
                return JsonValue.Create(resolved);
            default:
                return node;
        }
    }
}
