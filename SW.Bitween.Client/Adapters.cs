using System.Text.Json.Nodes;

namespace SW.Bitween.Client;

/// <summary>A Bitween's adapters, as their catalog describes them. Read only.</summary>
public sealed class Adapters
{
    readonly BitweenClient bitween;

    internal Adapters(BitweenClient bitween) => this.bitween = bitween;

    /// <summary>
    /// An adapter version's manifest, whole and as written - the sections Bitween itself does not read
    /// included - or null when there is none: an adapter published without one, a version never
    /// published, or one withdrawn. <paramref name="version"/> left out reads the current version.
    /// </summary>
    public Task<JsonObject?> GetManifestAsync(string adapterId, string? version = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        var path = $"api/adapters/manifest?adapterId={Uri.EscapeDataString(adapterId)}";
        if (!string.IsNullOrWhiteSpace(version)) path += $"&version={Uri.EscapeDataString(version)}";
        return bitween.GetOrNullAsync<JsonObject>(path, cancellation);
    }
}
