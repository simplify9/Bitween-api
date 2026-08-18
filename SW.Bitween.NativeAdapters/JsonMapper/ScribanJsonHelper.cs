using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace SW.Bitween.NativeAdapters.JsonMapper;

public static class ScribanJsonHelper
{
    /// <summary>
    /// Renders a Scriban template against the provided input JSON and returns the mapped output JSON.
    /// </summary>`
    public static string Render(string scribanTemplate, string inputJson)
    {
        var rendered = RenderText(scribanTemplate, inputJson);

        // 6. Strip trailing commas that may appear after the last field/element
        rendered = Regex.Replace(rendered, @",(\s*[}\]])", "$1");

        // 7. Parse rendered output — root may be an object OR an array
        JToken renderedToken;
        try
        {
            renderedToken = JToken.Parse(rendered);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Template produced invalid JSON: {ex.Message}\n\nRendered:\n{rendered}");
        }

        // 8. Expand dotted keys into nested objects recursively at all depths
        return ExpandDottedKeys(renderedToken).ToString(Formatting.Indented);
    }

    /// <summary>
    /// Renders a Scriban template against the provided input JSON and returns the text as-is,
    /// without requiring the result to be JSON.
    /// </summary>
    /// <remarks>
    /// For templates whose output is prose rather than a payload — an email subject or body, say.
    /// <see cref="Render"/> builds on this and adds the JSON validation and dotted-key expansion
    /// that a mapper needs and a sentence does not.
    /// </remarks>
    public static string RenderText(string scribanTemplate, string inputJson)
    {
        // 1. Parse input JSON — handle both root object and root array
        var rootToken = JToken.Parse(inputJson);

        // 2. Build top-level ScriptObject from input (recursive)
        ScriptObject scriptObj;
        if (rootToken is JArray rootArray)
        {
            // Expose the array under "items" and also forward member access to the
            // first element so templates can write either:
            //   {{ items[0].OrderId }}  or  {{ for item in items }} ... {{ end }}
            scriptObj = new ScriptObject();
            var smartArray = new SmartArray(rootArray.Select(ToScribanValue));
            scriptObj["items"] = smartArray;

            // If the first element is an object, also hoist its properties to the
            // top level so templates that reference fields directly still work.
            if (rootArray.Count > 0 && rootArray[0] is JObject firstObj)
            {
                var firstSo = BuildScriptObject(firstObj);
                foreach (var key in firstSo.Keys.ToList())
                    scriptObj.TrySetValue(null!, default, key, firstSo[key], false);
            }
        }
        else if (rootToken is JObject rootObj)
        {
            scriptObj = BuildScriptObject(rootObj);
        }
        else
        {
            throw new InvalidOperationException("Input JSON must be a root object or root array.");
        }

        // 3. Register custom functions (| json pipe, | to_float cast)
        var functions = new ScriptObject();
        functions.Import("json", new Func<object?, string>(JsonFilter));
        functions.Import("to_float", new Func<object?, object?>(val =>
        {
            if (val == null) return null;
            if (val is double or float or int or long or decimal) return Convert.ToDouble(val);
            var s = Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture);
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (object?)null;
        }));

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

        return template.Render(context);
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
            var value = ToScribanValue(prop.Value);

            // Always store with the original key so templates that reference the
            // exact JSON casing (e.g. partner props like "CustomerId") work correctly.
            so[prop.Name] = value;

            // Also register a first-char-lowercased alias so that templates written
            // before this fix (which relied on camelCase normalisation) continue to work.
            if (prop.Name.Length > 0 && char.IsUpper(prop.Name[0]))
            {
                var lcKey = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
                if (!so.ContainsKey(lcKey))
                    so[lcKey] = value;
            }
        }
        return so;
    }

    private static object? ToScribanValue(JToken token) => token switch
    {
        JObject o => BuildScriptObject(o),
        JArray a => new SmartArray(a.Select(ToScribanValue)),
        JValue v => v.Value,
        _ => null
    };

    /// <summary>
    /// A Scriban array that also delegates member access to its first element,
    /// so templates can write either <c>data[0].field</c> or <c>data.field</c>
    /// when the source JSON value is a single-element (or first-item) array.
    /// </summary>
    private sealed class SmartArray : ScriptArray
    {
        public SmartArray(IEnumerable<object?> items) : base(items) { }

        public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
        {
            if (base.TryGetValue(context, span, member, out value))
                return true;

            if (Count > 0 && this[0] is ScriptObject first)
                return first.TryGetValue(context, span, member, out value);

            value = null;
            return false;
        }
    }

    /// <summary>Recursively expands dotted keys in all JObjects at every depth, including inside arrays.</summary>
    private static JToken ExpandDottedKeys(JToken token)
    {
        if (token is JObject obj)
        {
            var result = new JObject();
            foreach (var prop in obj.Properties())
                SetByPath(result, prop.Name, ExpandDottedKeys(prop.Value));
            return result;
        }
        if (token is JArray arr)
        {
            var result = new JArray();
            foreach (var item in arr)
                result.Add(ExpandDottedKeys(item));
            return result;
        }
        return token;
    }

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
