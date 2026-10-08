using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>A document that does not match the schema of its information type.</summary>
public class DocumentSchemaException(string documentName, IReadOnlyList<string> errors)
    : Exception($"The document does not match the schema of '{documentName}':\n- " + string.Join("\n- ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// The schema an information type can carry — JSON Schema for a JSON type, XSD for an XML one —
/// and checking a document against it.
/// </summary>
/// <remarks>
/// A schema may only refer to itself: a <c>$ref</c> to a URL or file, or an XSD import or include,
/// would have the server fetch whatever an editor typed in. Compiled schemas are kept by their
/// text, so an edit is simply a new entry.
/// </remarks>
public static class DocumentSchema
{
    public const int MaxErrors = 20;

    /// <summary>The property the older partner endpoint adds to what it was sent. Not the partner's.</summary>
    private const string InjectedProperty = "_ExternalRequestContext";

    private static readonly ConcurrentDictionary<string, Lazy<Task<object>>> Compiled = new();

    /// <summary>Refuses a schema that can't be used for the format; null or empty is no schema.</summary>
    public static async Task EnsureUsable(DocumentFormat format, string schema)
    {
        if (string.IsNullOrWhiteSpace(schema)) return;
        if (format is not (DocumentFormat.Json or DocumentFormat.Xml))
            throw new SWValidationException("SCHEMA_NOT_SUPPORTED",
                "Only JSON and XML information types can carry a schema.");
        try
        {
            await CompileAsync(format, schema);
        }
        catch (Exception ex) when (ex is not SWValidationException)
        {
            throw new SWValidationException("INVALID_SCHEMA",
                (format == DocumentFormat.Json ? "Not a usable JSON Schema: " : "Not a usable XSD: ") + ex.Message);
        }
    }

    /// <summary>What is wrong with <paramref name="data"/>, at most <see cref="MaxErrors"/>; empty when it matches.</summary>
    public static async Task<IReadOnlyList<string>> Check(DocumentFormat format, string schema, string data)
    {
        if (string.IsNullOrWhiteSpace(schema) || format is not (DocumentFormat.Json or DocumentFormat.Xml))
            return [];

        var compiled = await CompileAsync(format, schema);
        return format == DocumentFormat.Json
            ? CheckJson((JsonSchema)compiled, data)
            : CheckXml((XmlSchemaSet)compiled, data);
    }

    /// <summary>Throws <see cref="DocumentSchemaException"/> when the document doesn't match.</summary>
    public static async Task Enforce(Domain.Document document, string data)
    {
        if (document is null) return;
        var errors = await Check(document.DocumentFormat, document.ValidationSchema, data);
        if (errors.Count > 0) throw new DocumentSchemaException(document.Name, errors);
    }

    private static async Task<object> CompileAsync(DocumentFormat format, string schema)
    {
        if (Compiled.Count > 1000) Compiled.Clear();
        var key = $"{format}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)))}";
        var entry = Compiled.GetOrAdd(key, _ => new Lazy<Task<object>>(async () => format == DocumentFormat.Json
            ? await CompileJson(schema)
            : CompileXml(schema)));
        try
        {
            return await entry.Value;
        }
        catch
        {
            // A schema that failed to compile is not kept, so fixing it is not answered from the cache.
            Compiled.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<object> CompileJson(string schema) =>
        await JsonSchema.FromJsonAsync(schema, null,
            root => new LocalOnlyResolver(new JsonSchemaAppender(root, new DefaultTypeNameGenerator())),
            CancellationToken.None);

    private static XmlSchemaSet CompileXml(string schema)
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(schema),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var compiled = XmlSchema.Read(reader, null)!;
        if (compiled.Includes.Count > 0)
            throw new SWValidationException("INVALID_SCHEMA",
                "The XSD imports or includes another schema. Put everything it needs in this one.");
        set.Add(compiled);
        set.Compile();
        return set;
    }

    private static IReadOnlyList<string> CheckJson(JsonSchema schema, string data)
    {
        JToken token;
        try
        {
            token = JToken.Parse(data);
        }
        catch (JsonReaderException ex)
        {
            return [$"Not valid JSON: {ex.Message}"];
        }

        if (token is JObject obj && obj.ContainsKey(InjectedProperty))
        {
            obj = (JObject)obj.DeepClone();
            obj.Remove(InjectedProperty);
            token = obj;
        }

        return schema.Validate(token).SelectMany(Flatten).Take(MaxErrors).ToList();

        static IEnumerable<string> Flatten(NJsonSchema.Validation.ValidationError error) =>
            error is NJsonSchema.Validation.ChildSchemaValidationError child && child.Errors.Count > 0
                ? child.Errors.SelectMany(e => e.Value).SelectMany(Flatten)
                : [$"{(string.IsNullOrEmpty(error.Path) ? "#" : error.Path)}: {error.Kind}"];
    }

    private static IReadOnlyList<string> CheckXml(XmlSchemaSet schemas, string data)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            if (errors.Count < MaxErrors)
                errors.Add($"line {e.Exception?.LineNumber}, position {e.Exception?.LinePosition}: {e.Message}");
        };

        // An XmlSchemaSet is not safe to share between threads.
        lock (schemas)
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(data), settings);
                while (reader.Read())
                {
                }
            }
            catch (XmlException ex)
            {
                errors.Add($"Not valid XML: {ex.Message}");
            }
        }

        return errors;
    }

    private class LocalOnlyResolver(JsonSchemaAppender appender) : JsonReferenceResolver(appender)
    {
        public override Task<NJsonSchema.References.IJsonReference> ResolveFileReferenceAsync(string filePath,
            CancellationToken cancellationToken = default) =>
            throw new SWValidationException("INVALID_SCHEMA",
                $"The schema refers to '{filePath}'. Put everything it needs in this one.");

        public override Task<NJsonSchema.References.IJsonReference> ResolveUrlReferenceAsync(string url,
            CancellationToken cancellationToken = default) =>
            throw new SWValidationException("INVALID_SCHEMA",
                $"The schema refers to '{url}'. Put everything it needs in this one.");
    }
}
