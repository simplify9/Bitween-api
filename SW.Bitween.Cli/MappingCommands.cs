using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CommandLine;
using SW.Bitween.Cli.Configuration;

namespace SW.Bitween.Cli;

[Verb("test", HelpText = "Map sample documents with subscriptions' saved mappings and compare them with what's expected.")]
public class MappingTestOptions
{
    [Value(0, MetaName = "cases", Required = true, HelpText = "The cases: a .yaml file listing subscription, input and expected.")]
    public string Cases { get; set; }

    [Option("update", HelpText = "Write each output over its expected file instead of comparing, to start or refresh them.")]
    public bool Update { get; set; }

    [Option("profile", HelpText = "Which signed-in Bitween; the current one, or BITWEEN_URL with BITWEEN_TOKEN, unless given.")]
    public string Profile { get; set; }
}

/// <summary>
/// CommandLineParser treats a single verb as the default, so 'bitween mapping' with no verb would run
/// the test with no file. A second, hidden verb keeps the verb required.
/// </summary>
[Verb("help-mapping", Hidden = true)]
public class MappingPlaceholderOptions;

public static partial class Commands
{
    /// <summary>
    /// Maps each case's input with its subscription's mapping, as saved on the Bitween, through the
    /// same preview the mapping editor uses, which runs the pipeline's own read, map and write. Nothing
    /// is delivered: only the mapping step is checked.
    /// </summary>
    /// <remarks>
    /// A case file:
    /// <code>
    /// cases:
    ///   - name: An order maps to the warehouse's shape
    ///     subscription: Orders to warehouse
    ///     input: samples/order.json
    ///     expected: samples/order.expected.json
    ///     partner: Acme marketplace     # whose values to map with; the subscription's own when left out
    /// </code>
    /// Paths are relative to the file. JSON is compared as JSON, so spacing and key order don't count.
    /// </remarks>
    public static async Task<int> TestMappings(MappingTestOptions opts, Profiles profiles)
    {
        var file = Path.GetFullPath(opts.Cases);
        if (!File.Exists(file))
        {
            Console.WriteLine($"{opts.Cases} doesn't exist.");
            return Failure;
        }

        var folder = Path.GetDirectoryName(file)!;
        var cases = (Yaml.Read(await File.ReadAllTextAsync(file)).FirstOrDefault()?["cases"] as JsonArray)?
            .OfType<JsonObject>().ToList() ?? [];
        if (cases.Count == 0)
        {
            Console.WriteLine($"{opts.Cases} lists no cases under 'cases'.");
            return Failure;
        }

        using var api = Api(profiles, opts.Profile);
        if (api == null) return Failure;

        int passed = 0, failed = 0;
        foreach (var c in cases)
        {
            var name = (string)c["name"] ?? (string)c["input"] ?? "(unnamed)";
            try
            {
                var outcome = await RunCase(api, folder, c, opts.Update);
                Console.WriteLine($"{(outcome == null ? "PASS" : "FAIL")}  {name}");
                if (outcome == null) passed++;
                else
                {
                    failed++;
                    foreach (var line in outcome.Split('\n')) Console.WriteLine($"      {line}");
                }
            }
            catch (Exception ex) when (ex is BitweenApiException or IOException or JsonException or InvalidOperationException
                                           or System.Xml.XmlException)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine($"      {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(opts.Update
            ? $"{passed} expected file(s) written, {failed} case(s) failed."
            : $"{passed} passed, {failed} failed.");
        return failed == 0 ? Success : Failure;
    }

    /// <returns>Null when the output is as expected, or what differs.</returns>
    static async Task<string> RunCase(BitweenApi api, string folder, JsonObject c, bool update)
    {
        var subscriptionName = (string)c["subscription"] ?? throw new InvalidOperationException("The case names no subscription.");
        var input = await File.ReadAllTextAsync(Path.Combine(folder, (string)c["input"] ?? throw new InvalidOperationException("The case names no input.")));
        var expectedPath = Path.Combine(folder, (string)c["expected"] ?? throw new InvalidOperationException("The case names no expected file."));

        var subscriptionId = await IdByName(api, "subscriptions", subscriptionName);
        var subscription = (JsonObject)await api.GetAsync($"api/subscriptions/{subscriptionId}");
        var mapperId = (string)subscription["mapperId"];
        var properties = (subscription["mapperProperties"] as JsonArray)?.OfType<JsonObject>()
            .ToDictionary(p => (string)p["key"] ?? "", p => (string)p["value"], StringComparer.OrdinalIgnoreCase) ?? [];
        var partnerId = c["partner"] is null
            ? subscription["partnerId"]?.GetValue<int?>()
            : await IdByName(api, "partners", (string)c["partner"]);

        string output;
        if (string.Equals(mapperId, "NativeMapper", StringComparison.OrdinalIgnoreCase))
        {
            var result = await api.PostAsync("api/mappingpreviews", new
            {
                mappingRules = properties.GetValueOrDefault("MappingRules") ?? "",
                sourceDocument = input,
                partnerId,
            });
            var errors = (result["ruleErrors"] as JsonArray)?.OfType<JsonObject>()
                .Select(e => $"{(string)e["target"]}: {(string)e["reason"]}").ToList() ?? [];
            if (result["error"] is { } error) return (string)error;
            if (errors.Count > 0) return "Rules that couldn't be applied:\n" + string.Join("\n", errors);
            output = (string)result["outputDocument"] ?? "";
        }
        else if (string.Equals(mapperId, "NativeJSONMapper", StringComparison.OrdinalIgnoreCase))
        {
            var result = await api.PostAsync("api/mappers", new
            {
                scribanTemplate = properties.GetValueOrDefault("ScribanTemplate") ?? "{}",
                inputJson = input,
                partnerId,
            });
            if (result["error"] is { } error && !string.IsNullOrEmpty((string)error)) return (string)error;
            output = (string)result["outputJson"] ?? "";
        }
        else
        {
            return mapperId == null
                ? $"{subscriptionName} has no mapper, so there is nothing to test."
                : $"{subscriptionName} maps with {mapperId}. Only the built-in mappers can be tested here; test a custom one with 'bitween adapter run'.";
        }

        if (update)
        {
            await File.WriteAllTextAsync(expectedPath, Pretty(output));
            return null;
        }

        var expected = await File.ReadAllTextAsync(expectedPath);
        return Difference(expected, output);
    }

    static async Task<int> IdByName(BitweenApi api, string resource, string name)
    {
        var found = await api.GetAsync($"api/{resource}?filter=Name:1:{Uri.EscapeDataString(name)}&size=50");
        var match = (found["result"] as JsonArray)?.OfType<JsonObject>()
            .Where(r => string.Equals((string)r["name"], name, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        return match.Count switch
        {
            1 => (int)match[0]["id"]!,
            0 => throw new InvalidOperationException($"There is no {resource.TrimEnd('s')} named '{name}'."),
            _ => throw new InvalidOperationException($"{match.Count} {resource} are named '{name}'; rename one so a case can name it."),
        };
    }

    /// <returns>Null when the two are the same document, or a description of the first difference.</returns>
    static string Difference(string expected, string actual)
    {
        if (TryJson(expected, out var e) && TryJson(actual, out var a))
            return JsonDifference("$", e, a);
        if (TryXml(expected, out var ex) && TryXml(actual, out var ax))
            return XNode.DeepEquals(ex, ax) ? null : $"The XML differs.\nexpected: {ex}\nactual:   {ax}";
        return expected.Trim() == actual.Trim() ? null : $"expected: {expected.Trim()}\nactual:   {actual.Trim()}";
    }

    static string JsonDifference(string path, JsonNode expected, JsonNode actual)
    {
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            foreach (var key in eo.Select(p => p.Key).Union(ao.Select(p => p.Key)))
            {
                if (!ao.ContainsKey(key)) return $"{path}.{key} is missing.";
                if (!eo.ContainsKey(key)) return $"{path}.{key} is there and isn't expected: {ao[key]?.ToJsonString()}";
                var inner = JsonDifference($"{path}.{key}", eo[key], ao[key]);
                if (inner != null) return inner;
            }
            return null;
        }

        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            if (ea.Count != aa.Count) return $"{path} has {aa.Count} item(s); {ea.Count} expected.";
            for (var i = 0; i < ea.Count; i++)
            {
                var inner = JsonDifference($"{path}[{i}]", ea[i], aa[i]);
                if (inner != null) return inner;
            }
            return null;
        }

        return JsonNode.DeepEquals(expected, actual)
            ? null
            : $"{path} is {actual?.ToJsonString() ?? "null"}; {expected?.ToJsonString() ?? "null"} expected.";
    }

    static bool TryJson(string text, out JsonNode node)
    {
        try
        {
            node = JsonNode.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    static bool TryXml(string text, out XElement element)
    {
        try
        {
            element = XElement.Parse(text);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            element = null;
            return false;
        }
    }

    static string Pretty(string output) =>
        TryJson(output, out var node) ? node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null" : output;
}
