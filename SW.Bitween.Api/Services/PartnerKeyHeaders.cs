using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SW.Bitween;

/// <summary>
/// The header a partner's key can travel in besides Bearer and Basic. Named once for the whole
/// system on the settings page, and a gateway can name its own. <c>partnerkey</c> is always
/// accepted as well, so renaming the header doesn't cut off partners still sending the old one.
/// </summary>
public static partial class PartnerKeyHeaders
{
    public const string Default = "partnerkey";

    // Headers HTTP or the gateway already gives a meaning: a key sent in one would be read as that.
    static readonly HashSet<string> Taken = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "content-type", "content-length", "host", "cookie", "connection",
        "transfer-encoding", "wait-period", "waitresponse", "request-context-correlation-id",
    };

    // An HTTP token (RFC 9110): no spaces, no separators.
    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,100}\z")]
    private static partial Regex Token();

    /// <returns>Why the name can't be used, or null when it can.</returns>
    public static string Problem(string name) =>
        string.IsNullOrWhiteSpace(name) ? "A header name is required."
        : !Token().IsMatch(name.Trim()) ? $"'{name}' can't be a header name. Use letters, digits and - or _, with no spaces."
        : Taken.Contains(name.Trim()) ? $"'{name.Trim()}' already means something to HTTP or to Bitween, so a key sent in it would be read as that."
        : null;
}
