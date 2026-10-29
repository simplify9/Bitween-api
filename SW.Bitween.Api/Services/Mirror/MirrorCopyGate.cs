using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Tells a save that recreates prod's setup, during a mirror's copy, from one a person makes.
/// <para>
/// Some rules leave alone what is already saved and refuse it only on a new save: a response handed
/// to an Internal or ApiCall subscription, from before Response subscriptions existed, is the one
/// today. A copy is a new save of something prod already has, so it is let through the way prod's own
/// is. The copy's calls to this Bitween carry a key it opens for the length of the copy; a save is
/// treated as recreating prod only when it carries an open key, on a Bitween marked as a mirror.
/// People editing on staging never carry one, so they meet the same rules as on prod.
/// </para>
/// </summary>
public sealed class MirrorCopyGate(IHttpContextAccessor httpContextAccessor, BitweenOptions options)
{
    public const string Header = "X-Bitween-Mirror-Copy";

    static readonly ConcurrentDictionary<string, byte> Open = new(StringComparer.Ordinal);

    /// <summary>A key for one copy's calls; close it when the copy ends.</summary>
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

    /// <summary>Whether the request being handled is a mirror's copy recreating what prod already has.</summary>
    public bool RecreatingProd =>
        options.Mirror.Target &&
        httpContextAccessor.HttpContext?.Request.Headers[Header].ToString() is { Length: > 0 } key &&
        Open.ContainsKey(key);
}
