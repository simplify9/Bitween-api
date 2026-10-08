using System;

namespace SW.Bitween;

/// <summary>
/// This Bitween's version, for an adapter manifest's <c>compatibility.minBitweenVersion</c>.
/// </summary>
/// <remarks>
/// The assemblies are built as 1.0.0, so the release cannot be read from them. A deployment's
/// pipeline can set <c>Bitween:Version</c>; otherwise the baseline here stands, raised by hand with
/// each release that adds something an adapter might depend on.
/// </remarks>
public static class BitweenInfo
{
    public const string Baseline = "10.0.0";

    public static Version Version(BitweenOptions options) =>
        System.Version.TryParse(options?.Version?.Split('+', '-')[0], out var configured)
            ? configured
            : System.Version.Parse(Baseline);

    /// <summary>True when <paramref name="minimum"/> is empty, unreadable, or not above this Bitween.</summary>
    public static bool Satisfies(string minimum, BitweenOptions options)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        var core = minimum.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return !System.Version.TryParse(core, out var required) || required <= Version(options);
    }
}
