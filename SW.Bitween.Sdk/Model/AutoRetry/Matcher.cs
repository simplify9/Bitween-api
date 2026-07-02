using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SW.Bitween.Model;

/// <summary>
/// Comparison operator used by <see cref="JsonPathMatcher"/>.
/// </summary>
public enum JsonPathOp
{
    /// <summary>Exact, case-insensitive equality.</summary>
    Eq,

    /// <summary>Not equal (case-insensitive).</summary>
    Neq,

    /// <summary>The actual value contains the expected substring (case-insensitive).</summary>
    Contains,

    /// <summary>The actual value matches the expected regular expression (case-insensitive).</summary>
    Regex,

    /// <summary>The JSON node exists at the path (any value, including null).</summary>
    Exists,

    /// <summary>The JSON node does not exist at the path.</summary>
    NotExists,
}

/// <summary>
/// Tests raw failure content and returns whether it matches a specific pattern.
/// </summary>
/// <remarks>
/// For <see cref="XchangeResultType.Error"/> groups the content is the exception stack-trace text.
/// For <see cref="XchangeResultType.BadResult"/> groups the content is the raw JSON response string.
/// Matcher implementations are serialised polymorphically via <c>System.Text.Json</c>.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ContainsMatcher),      typeDiscriminator: "contains")]
[JsonDerivedType(typeof(RegexMatcher),         typeDiscriminator: "regex")]
[JsonDerivedType(typeof(ExceptionTypeMatcher), typeDiscriminator: "exceptionType")]
[JsonDerivedType(typeof(JsonPathMatcher),      typeDiscriminator: "jsonPath")]
public abstract class Matcher
{
    /// <summary>The result type this matcher operates on.</summary>
    public abstract XchangeResultType ResultType { get; }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="content"/> satisfies this matcher's condition.
    /// Implementations must never throw — malformed input should return <c>false</c>.
    /// </summary>
    public abstract bool IsMatch(string content);
}

// ── Error matchers ────────────────────────────────────────────────────────────

/// <summary>
/// Matches when the exception text contains a literal substring.
/// Applies to <see cref="XchangeResultType.Error"/> content.
/// </summary>
public class ContainsMatcher : Matcher
{
    /// <inheritdoc/>
    public override XchangeResultType ResultType => XchangeResultType.Error;

    /// <summary>The substring to search for.</summary>
    public required string Value { get; init; }

    /// <summary>When <c>true</c> the comparison is case-sensitive. Defaults to <c>false</c>.</summary>
    public bool CaseSensitive { get; init; } = false;

    /// <inheritdoc/>
    public override bool IsMatch(string content) =>
        content.Contains(Value,
            CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Matches when the exception text satisfies a regular expression.
/// Applies to <see cref="XchangeResultType.Error"/> content.
/// </summary>
public class RegexMatcher : Matcher
{
    /// <inheritdoc/>
    public override XchangeResultType ResultType => XchangeResultType.Error;

    /// <summary>.NET-compatible regular expression pattern.</summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Modifier flags. Supported: <c>"i"</c> (case-insensitive). Defaults to <c>"i"</c>.
    /// Pass an empty string for case-sensitive matching.
    /// </summary>
    public string Flags { get; init; } = "i";

    private Regex? _compiled;

    private Regex Compiled => _compiled ??= new Regex(
        Pattern,
        Flags.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None,
        matchTimeout: TimeSpan.FromMilliseconds(200));

    /// <inheritdoc/>
    public override bool IsMatch(string content) => Compiled.IsMatch(content);
}

/// <summary>
/// Matches when the exception text mentions a specific .NET exception type name,
/// scanning the entire stack-trace including inner exceptions.
/// Applies to <see cref="XchangeResultType.Error"/> content.
/// </summary>
/// <example>
/// <c>Value = "System.TimeoutException"</c> fires on any stack trace that contains that
/// fully-qualified type name.
/// </example>
public class ExceptionTypeMatcher : Matcher
{
    /// <inheritdoc/>
    public override XchangeResultType ResultType => XchangeResultType.Error;

    /// <summary>
    /// Fully-qualified or short exception type name, e.g. <c>"System.TimeoutException"</c>
    /// or <c>"SqlException"</c>. The regex extraction captures segments matching
    /// <c>([\w\.]+Exception)</c>.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// When <c>true</c> (default) the full stack trace — including inner exceptions — is
    /// scanned. When <c>false</c> only the first type name in the text is checked.
    /// </summary>
    public bool IncludeInner { get; init; } = true;

    private static readonly Regex TypePattern =
        new(@"([\w\.]+Exception)", RegexOptions.Compiled);

    /// <inheritdoc/>
    public override bool IsMatch(string content)
    {
        foreach (Match m in TypePattern.Matches(content))
        {
            if (m.Value.Equals(Value, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IncludeInner) break;
        }
        return false;
    }
}

// ── BadResult matcher ─────────────────────────────────────────────────────────

/// <summary>
/// Evaluates a JSONPath expression against a bad-result payload.
/// Applies to <see cref="XchangeResultType.BadResult"/> content.
/// </summary>
/// <remarks>
/// Supports dot-notation paths and array indexers, e.g. <c>$.error.code</c> and
/// <c>$.lines[0].status</c>. Invalid JSON or a missing path returns <c>false</c> without
/// throwing. For production use consider replacing <c>ResolvePath</c> with
/// <c>JsonPath.Net</c> or Newtonsoft's <c>SelectToken</c>.
/// </remarks>
public class JsonPathMatcher : Matcher
{
    /// <inheritdoc/>
    public override XchangeResultType ResultType => XchangeResultType.BadResult;

    /// <summary>JSONPath expression, e.g. <c>"$.error.code"</c> or <c>"$.lines[0].status"</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Comparison operation to apply once the node is located.</summary>
    public JsonPathOp Op { get; init; }

    /// <summary>Expected value. Not used when <see cref="Op"/> is <see cref="JsonPathOp.Exists"/> or <see cref="JsonPathOp.NotExists"/>.</summary>
    public string? Value { get; init; }

    /// <inheritdoc/>
    public override bool IsMatch(string content)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch { return false; }

        var node = ResolvePath(root, Path);

        return Op switch
        {
            JsonPathOp.Exists    => node is not null,
            JsonPathOp.NotExists => node is null,
            _ => node is not null && Compare(node.ToString(), Value ?? "", Op)
        };
    }

    private static bool Compare(string actual, string expected, JsonPathOp op) => op switch
    {
        JsonPathOp.Eq       => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
        JsonPathOp.Neq      => !actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
        JsonPathOp.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        JsonPathOp.Regex    => Regex.IsMatch(actual, expected, RegexOptions.IgnoreCase),
        _ => false
    };

    private static JsonNode? ResolvePath(JsonNode? root, string path)
    {
        var segments = path.TrimStart('$').TrimStart('.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var segment in segments)
        {
            if (current is null) return null;

            var arrayMatch = Regex.Match(segment, @"^(\w+)\[(\d+)\]$");
            if (arrayMatch.Success)
            {
                current = current[arrayMatch.Groups[1].Value];
                if (current is JsonArray arr &&
                    int.TryParse(arrayMatch.Groups[2].Value, out var idx))
                    current = idx < arr.Count ? arr[idx] : null;
            }
            else
            {
                current = current[segment];
            }
        }
        return current;
    }
}
