using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// A rehearsal's traffic on a mirror, told apart by its correlation id, which every exchange of a family
/// carries on: the document routed, the exchanges it starts, the responses they hand on.
/// <para>
/// Staging's subscriptions stay paused, so nothing runs on its own; rehearsal traffic alone gets
/// through, as prod's traffic gets through prod's subscriptions. Nor is it ever a duplicate of the
/// rehearsal before it, which sent the same documents. Calls through a door - an API gateway, the
/// partner API - carry a key the rehearsal opens for as long as it runs.
/// </para>
/// </summary>
public static class MirrorTraffic
{
    internal const string Prefix = "rehearsal-";

    public const string Header = "X-Bitween-Mirror-Rehearsal";

    /// <summary>The value a rehearsal's message carries its correlation in, across the bus.</summary>
    public const string BusValue = "BitweenMirrorRehearsal";

    static readonly ConcurrentDictionary<string, byte> Open = new(StringComparer.Ordinal);

    public static string NewCorrelation() => Prefix + Guid.NewGuid().ToString("N");

    /// <summary>Rehearsal traffic, on a Bitween marked as a mirror.</summary>
    public static bool IsRehearsal(BitweenOptions options, string correlationId) =>
        options.Mirror.Target && correlationId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// A paused scheduled job's or aggregation's run on its schedule, on a mirror, which holds it back.
    /// Bitween otherwise runs a paused one's schedule, so a brought receiver would poll the team's test
    /// system on its own. Receive now, Roll up now and a test have no schedule, and run.
    /// </summary>
    public static bool HoldsScheduledRun(BitweenOptions options, Subscription subscription, string cronExpression) =>
        options?.Mirror.Target == true && subscription.PausedOn != null && cronExpression != null;

    /// <summary>A key for one rehearsal's calls through a door; close it when the rehearsal ends.</summary>
    public static string OpenKey()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Open[key] = 0;
        return key;
    }

    public static void CloseKey(string key)
    {
        if (key != null) Open.TryRemove(key, out _);
    }

    /// <summary>
    /// The correlation id for a call through a door: a rehearsal's own when the call carries an open
    /// key, on a mirror, naming the correlation the rehearsal chose; null for anyone else's.
    /// </summary>
    public static string CorrelationFor(BitweenOptions options, IHeaderDictionary headers)
    {
        if (!options.Mirror.Target || headers?[Header].ToString() is not { Length: > 0 } value) return null;
        var at = value.IndexOf(' ');
        if (at < 0 || !Open.ContainsKey(value[..at])) return null;
        var correlation = value[(at + 1)..].Trim();
        return correlation.StartsWith(Prefix, StringComparison.Ordinal) && correlation.Length <= 64 ? correlation : null;
    }

    // ------------------------------------------------------------------ where it goes

    static volatile IReadOnlySet<int> switchedOff;

    /// <summary>
    /// The bus gateways a rehearsal's traffic routes through though the copy switched them off: those
    /// prod has on. Null while no rehearsal runs.
    /// </summary>
    public static IReadOnlySet<int> SwitchedOffGateways => switchedOff;

    internal static void RouteThrough(IReadOnlySet<int> gateways) => switchedOff = gateways;
}
