using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween;

/// <summary>
/// What startup properties an adapter expects — its key names, which are optional, which are
/// secret, and their defaults.
/// </summary>
/// <remarks>
/// <para>
/// Answering this means knowing whether the adapter runs in-process or is published to storage and
/// run in a child process, and that fork was written out six times across the adapter and
/// subscription resources before this existed.
/// </para>
/// <para>
/// It is a schema, not data: nothing a user does in the UI can change it, because a subscription's
/// actual property <em>values</em> live in the database and are never part of this. It changes only
/// when a new adapter package is uploaded. <see cref="ServerlessAdapterDescriber"/> is what makes
/// use of that.
/// </para>
/// </remarks>
public class AdapterStartupValues(
    NativeAdapterDiscoveryService nativeAdapterDiscovery,
    ServerlessAdapterDescriber serverlessDescriber,
    IServiceProvider serviceProvider)
{
    /// <summary>Drops what is remembered about a published adapter.</summary>
    public void Forget(string adapterId) => serverlessDescriber.Forget(adapterId);

    /// <param name="adapterId">Native (<c>native</c> prefix) or published.</param>
    /// <returns>Key name to description. Empty when the adapter reports nothing.</returns>
    public async Task<IDictionary<string, StartupValue>> Describe(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
            return new Dictionary<string, StartupValue>();

        // Reflection over an in-process type, so there is nothing here worth caching, and nothing
        // worth queueing behind the published adapters either.
        if (adapterId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            return nativeAdapterDiscovery.GetStartupValues(adapterId);

        // A RESIDENT adapter cannot answer this, and the attempt is not harmless. Describing a
        // published adapter means spawning it and asking over stdio; a resident one dials out to
        // the host instead of speaking stdio, so the ask waits for a reply that never comes and
        // fails as "Received null data".
        //
        // Every caller of this then failed in its own way and none named a cause: saving a
        // subscription that used one was refused outright, and the two that mask secrets failed
        // closed and returned every property as "__private__" — so a screen showed a masked value
        // where the chosen statement should be, and its dropdown could not match it.
        //
        // Nothing is the right answer rather than a shrug. A resident adapter's settings live on
        // its DATA SOURCE, which describes and masks its own; what a subscription holds for one
        // is which statement to run and what to do with it — routing, not secrets.
        if (await ResidentAdapters.IsResidentAsync(serviceProvider, adapterId))
            return new Dictionary<string, StartupValue>();

        return await serverlessDescriber.Describe(adapterId);
    }

    /// <summary>
    /// The same description with the default withheld from every secret property — the shape to
    /// hand to a client.
    /// </summary>
    /// <remarks>
    /// A default is part of the adapter's package, not of any subscription, so masking a
    /// subscription's values never touches it. One adapter shipped a production storage key as
    /// the default of a secret property, and every screen that draws an adapter form showed it as
    /// that field's placeholder. Nothing a client does needs a secret's default: the adapter
    /// applies it itself when the property is left empty.
    /// <para>
    /// Copies rather than edits: <see cref="ServerlessAdapterDescriber"/> remembers what it
    /// returns, and the masking and validation callers read that same instance.
    /// </para>
    /// </remarks>
    public static IDictionary<string, StartupValue> WithoutSecretDefaults(IDictionary<string, StartupValue> values)
    {
        var result = new Dictionary<string, StartupValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            result[key] = value.Private
                ? new StartupValue
                {
                    Optional = value.Optional,
                    Type = value.Type,
                    Private = true,
                    Description = value.Description
                }
                : value;
        return result;
    }
}
