using System.Text;
using System.Text.RegularExpressions;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Normalizes an SSH private key coming from adapter settings into a shape SSH clients accept.
/// Shared by every native adapter that does key-based auth, so their handling can never diverge.
///
/// Rules:
///  - Blank input          -> empty string.
///  - Already multi-line    -> returned untouched (never risk corrupting a well-formed key).
///  - Flattened PEM (single line, has BEGIN/END markers) -> rebuilt with the header/footer
///    preserved exactly and the base64 body re-wrapped at 64 chars.
///  - Anything else (e.g. a flattened non-PEM format) -> returned as-is rather than mangled.
/// </summary>
public static class SshKeyNormalizer
{
    private static readonly Regex PemShape =
        new(@"^(-----BEGIN [^-]+-----)(.*?)(-----END [^-]+-----)$", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Normalize(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        var key = rawKey.Trim();

        // Well-formed multi-line key: trust it exactly as given.
        if (key.Contains('\n'))
            return key;

        // Single flattened line: only reconstruct if it's a PEM key we recognize.
        var match = PemShape.Match(key);
        if (!match.Success)
            return key;

        var header = match.Groups[1].Value.Trim();
        var body = Regex.Replace(match.Groups[2].Value, @"\s+", string.Empty);
        var footer = match.Groups[3].Value.Trim();

        var sb = new StringBuilder();
        sb.Append(header).Append('\n');
        for (var i = 0; i < body.Length; i += 64)
            sb.Append(body.Substring(i, Math.Min(64, body.Length - i))).Append('\n');
        sb.Append(footer).Append('\n');
        return sb.ToString();
    }
}
