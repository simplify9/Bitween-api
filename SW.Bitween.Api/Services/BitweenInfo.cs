using System;
using System.Reflection;

namespace SW.Bitween;

/// <summary>
/// This Bitween's version, for an adapter manifest's <c>compatibility.minBitweenVersion</c>.
/// </summary>
/// <remarks>
/// The version CI stamped on this build, its semantic version (the image's <c>BITWEEN_VERSION</c>), so a
/// container says which release it is wherever it runs, with nothing to configure. A build CI didn't
/// stamp, as a local one, is the baseline here, raised by hand with each release that adds something an
/// adapter might depend on. <c>Bitween:Version</c>, which deployments could set before builds were
/// stamped, still wins when one does. Read from this assembly, not the process's entry one, so whatever
/// hosts Bitween in a test isn't taken for it.
/// </remarks>
public static class BitweenInfo
{
    public const string Baseline = "10.0.0";

    /// <summary>What CI stamped on this build, as <c>10.0.93+abc1234...</c>; null for an unstamped one.</summary>
    public static string Build { get; } = Stamped(typeof(BitweenInfo).Assembly);

    static readonly Version StampedVersion = Core(Build);

    public static Version Version(BitweenOptions options) =>
        Core(options?.Version) ?? StampedVersion ?? System.Version.Parse(Baseline);

    /// <summary>The build's informational version, when it is a release rather than the 1.0.0 every unstamped build says.</summary>
    internal static string Stamped(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Core(informational) is { } version && version > System.Version.Parse(Baseline) ? informational : null;
    }

    static Version Core(string text) =>
        System.Version.TryParse(text?.Trim().TrimStart('v', 'V').Split('+', '-')[0], out var version) ? version : null;

    /// <summary>True when <paramref name="minimum"/> is empty, unreadable, or not above this Bitween.</summary>
    public static bool Satisfies(string minimum, BitweenOptions options)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        var core = minimum.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return !System.Version.TryParse(core, out var required) || required <= Version(options);
    }
}
