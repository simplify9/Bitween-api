using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public class NativeJsonFieldMapper : INativeInfolinkHandler
{
    public string Name => "NativeJsonFieldMapper";
    public Type StartupValuesType => typeof(JsonFieldMapperInput);

    private JsonFieldMapperInput _options = new();

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = new JsonFieldMapperInput
        {
            Rules = settings.TryGetValue("Rules", out var r) ? r : "[]"
        };
    }

    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var source = JObject.Parse(xchangeFile.Data);
        var rules = JsonConvert.DeserializeObject<List<MappingRule>>(_options.Rules) ?? new List<MappingRule>();
        var result = new JObject();

        var validRules = rules.Where(r => !string.IsNullOrWhiteSpace(r.OutputField)).ToList();

        // Split into scalar (no [*]) and array (contain [*])
        var scalarRules = validRules
            .Where(r => !r.OutputField.Contains("[*]") && (string.IsNullOrWhiteSpace(r.SourcePath) || !r.SourcePath.Contains("[*]")))
            .ToList();
        var arrayRules = validRules
            .Where(r => r.OutputField.Contains("[*]") || (!string.IsNullOrWhiteSpace(r.SourcePath) && r.SourcePath.Contains("[*]")))
            .ToList();

        // Process scalars in declaration order — preserves field position and handles both fixed and source-mapped
        foreach (var rule in scalarRules)
        {
            if (rule.FixedValue != null && string.IsNullOrWhiteSpace(rule.SourcePath))
                SetByPath(result, rule.OutputField, new JValue(rule.FixedValue));
            else if (!string.IsNullOrWhiteSpace(rule.SourcePath))
                SetByPath(result, rule.OutputField, GetByPath(source, rule.SourcePath));
        }

        var sourceMappedArrayRules = arrayRules.Where(r => !string.IsNullOrWhiteSpace(r.SourcePath)).ToList();
        var fixedArrayRules = arrayRules.Where(r => string.IsNullOrWhiteSpace(r.SourcePath) && r.FixedValue != null).ToList();

        var arrayGroups = sourceMappedArrayRules.GroupBy(r => (
            SourcePrefix: r.SourcePath.Split(new[] { "[*]" }, StringSplitOptions.None)[0].TrimEnd('.'),
            OutputPrefix: r.OutputField.Split(new[] { "[*]" }, StringSplitOptions.None)[0].TrimEnd('.')
        ));

        foreach (var group in arrayGroups)
        {
            if (GetByPath(source, group.Key.SourcePrefix) is not JArray sourceArray) continue;

            var groupFixed = fixedArrayRules
                .Where(r => r.OutputField.Split(new[] { "[*]" }, StringSplitOptions.None)[0].TrimEnd('.') == group.Key.OutputPrefix)
                .ToList();

            var outputArray = new JArray();
            foreach (var item in sourceArray)
            {
                var outputItem = new JObject();
                foreach (var fixedRule in groupFixed)
                {
                    var outParts = fixedRule.OutputField.Split(new[] { "[*]." }, StringSplitOptions.None);
                    if (outParts.Length >= 2)
                        SetByPath(outputItem, outParts[1], new JValue(fixedRule.FixedValue));
                }
                foreach (var rule in group)
                {
                    var srcParts = rule.SourcePath.Split(new[] { "[*]." }, StringSplitOptions.None);
                    var outParts = rule.OutputField.Split(new[] { "[*]." }, StringSplitOptions.None);
                    if (srcParts.Length < 2 || outParts.Length < 2) continue;
                    SetByPath(outputItem, outParts[1], GetByPath(item, srcParts[1]));
                }
                outputArray.Add(outputItem);
            }
            SetByPath(result, group.Key.OutputPrefix, outputArray);
        }

        return Task.FromResult(new XchangeFile(result.ToString(Formatting.None), xchangeFile.Filename));
    }

    private static JToken? GetByPath(JToken token, string path)
    {
        var parts = path.Split('.');
        JToken? current = token;
        foreach (var part in parts)
        {
            if (current is JObject jObj)
                current = jObj[part];
            else
                return null;
        }
        return current;
    }

    private static void SetByPath(JObject obj, string path, JToken? value)
    {
        var parts = path.Split('.');
        var current = obj;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JObject nested)
            {
                nested = new JObject();
                current[parts[i]] = nested;
            }
            current = nested;
        }
        current[parts[^1]] = value ?? JValue.CreateNull();
    }
}

public record MappingRule(string OutputField, string SourcePath, string? FixedValue = null);
