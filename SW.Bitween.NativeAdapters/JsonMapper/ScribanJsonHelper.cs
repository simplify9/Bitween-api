using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scriban;
using Scriban.Runtime;

namespace SW.Bitween.NativeAdapters.JsonMapper;

public static class ScribanJsonHelper
{
    /// <summary>
    /// Renders a Scriban template against the provided input JSON and returns the mapped output JSON.
    /// </summary>
    public static string Render(string scribanTemplate, string inputJson)
    {
        // 1. Parse input JSON
        var inputObj = JObject.Parse(inputJson);

        // 2. Build top-level ScriptObject from input (recursive)
        var scriptObj = BuildScriptObject(inputObj);

        // 3. Register custom functions (| json pipe)
        var functions = new ScriptObject();
        functions.Import("json", new Func<object?, string>(JsonFilter));

        // 4. Create template context
        var context = new TemplateContext { StrictVariables = false };
        context.PushGlobal(scriptObj);
        context.PushGlobal(functions);

        // 5. Parse and render Scriban template
        var template = Template.Parse(scribanTemplate);
        if (template.HasErrors)
        {
            var errors = string.Join("; ", template.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Template parse error: {errors}");
        }

        var rendered = template.Render(context);

        // 6. Strip trailing commas that may appear after the last field/element
        rendered = Regex.Replace(rendered, @",(\s*[}\]])", "$1");

        // 7. Parse rendered output as JToken
        JObject flat;
        try
        {
            flat = JObject.Parse(rendered);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Template produced invalid JSON: {ex.Message}\n\nRendered:\n{rendered}");
        }

        // 8. Expand dotted keys into nested objects (e.g. "buyer.email" → {buyer:{email:...}})
        var result = new JObject();
        foreach (var prop in flat.Properties())
        {
            SetByPath(result, prop.Name, prop.Value);
        }

        return result.ToString(Formatting.Indented);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Custom | json Scriban pipe — serializes any value to its JSON representation.</summary>
    private static string JsonFilter(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            int or long or float or double or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            string s => JsonConvert.SerializeObject(s),
            _ => JsonConvert.SerializeObject(value)
        };
    }

    /// <summary>Recursively converts a JObject into a Scriban ScriptObject.</summary>
    private static ScriptObject BuildScriptObject(JObject obj)
    {
        var so = new ScriptObject();
        foreach (var prop in obj.Properties())
        {
            so[prop.Name] = ToScribanValue(prop.Value);
        }
        return so;
    }

    private static object? ToScribanValue(JToken token) => token switch
    {
        JObject o => BuildScriptObject(o),
        JArray a => a.Select(ToScribanValue).ToList(),
        JValue v => v.Value,
        _ => null
    };

    /// <summary>Sets a value at a dot-separated path inside a JObject, creating intermediate objects as needed.</summary>
    private static void SetByPath(JObject root, string path, JToken value)
    {
        var parts = path.Split('.');
        JObject current = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (current[part] is not JObject child)
            {
                child = new JObject();
                current[part] = child;
            }
            current = child;
        }
        current[parts[^1]] = value;
    }
}
