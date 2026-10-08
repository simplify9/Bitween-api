using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// The HTTP adapters' Headers setting: <c>Name:Value</c> entries separated by commas, as it has
/// always been read.
/// </summary>
/// <remarks>
/// The setting's description promised <c>name=value</c> lines, and the way people naturally type
/// a list — a space after each comma — made a header name with a leading space, which .NET refuses:
/// every delivery failed with a FormatException. Both are read now, alongside the original form:
/// names and values are trimmed, newlines separate as commas do, empty entries are skipped, and
/// <c>=</c> separates an entry that has no colon. An entry that is neither says which it was.
/// </remarks>
internal static class HeaderList
{
    public static IEnumerable<KeyValuePair<string, string>> Parse(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) yield break;

        foreach (var entry in headers.Split([',', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // The first colon only: a value may hold one of its own (a URL, a time).
            var separator = entry.IndexOf(':');
            if (separator < 0) separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new SWException($"The header '{entry}' needs a name and a value, written Name:Value.");

            yield return new KeyValuePair<string, string>(entry[..separator].Trim(), entry[(separator + 1)..].Trim());
        }
    }
}
