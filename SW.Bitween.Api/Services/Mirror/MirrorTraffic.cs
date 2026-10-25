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
    const string Prefix = "rehearsal-";

    public const string Header = "X-Bitween-Mirror-Rehearsal";

    /// <summary>The value a rehearsal's message carries its correlation in, across the bus.</summary>
    public const string BusValue = "BitweenMirrorRehearsal";

    static readonly ConcurrentDictionary<string, byte> Open = new(StringComparer.Ordinal);

    public static string NewCorrelation() => Prefix + Guid.NewGuid().ToString("N");

    /// <summary>Rehearsal traffic, on a Bitween marked as a mirror.</summary>
    public static bool IsRehearsal(BitweenOptions options, string correlationId) =>
        options.Mirror.Target && correlationId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

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

    // ------------------------------------------------------------------ which exchange is running

    /// <summary>An exchange of a rehearsal while staging runs it.</summary>
    public sealed record Running(string Id, string Correlation, int? SubscriptionId);

    static readonly AsyncLocal<Running> current = new();
    static readonly ConcurrentDictionary<string, Running> running = new(StringComparer.Ordinal);

    /// <summary>The rehearsal exchange this code runs for, where Bitween runs its adapter itself.</summary>
    public static Running Current => current.Value;

    /// <summary>Every rehearsal exchange running now: an adapter in its own process says nothing of which it runs for.</summary>
    public static IReadOnlyList<Running> Now => running.Values.ToList();

    /// <summary>Marks a rehearsal exchange as running until disposed; nothing for any other exchange.</summary>
    public static IDisposable Track(BitweenOptions options, Xchange xchange) =>
        Track(options, xchange.Id, xchange.CorrelationId, xchange.SubscriptionId);

    internal static IDisposable Track(BitweenOptions options, string id, string correlationId, int? subscriptionId)
    {
        if (!IsRehearsal(options, correlationId)) return null;
        var entry = new Running(id, correlationId, subscriptionId);
        current.Value = entry;
        running[id] = entry;
        return new Untrack(id);
    }

    sealed class Untrack(string id) : IDisposable
    {
        public void Dispose()
        {
            running.TryRemove(id, out _);
            current.Value = null;
        }
    }
}
