using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SW.Bitween;

/// <summary>
/// Locks an account against one address after repeated wrong passwords from it, leaving every
/// other address free to sign in.
/// </summary>
/// <remarks>
/// <para>
/// The account-wide lockout alone let anyone who knew an address keep its owner out: five wrong
/// guesses from anywhere and the real user could not sign in for fifteen minutes, again and
/// again. Counting per address stops guessing from that address without punishing the person who
/// owns the account. The account-wide lockout stays as a backstop at a higher threshold, for
/// guessing spread across many addresses.
/// </para>
/// <para>
/// Held in memory, per replica. A guesser whose attempts are spread over replicas gets a few more
/// tries before each replica locks them out, and the backstop still counts every attempt in the
/// database. Losing these counts on a restart only costs an attacker's progress being forgotten,
/// which the backstop also covers.
/// </para>
/// </remarks>
public class SignInThrottle
{
    public const int PerAddressLimit = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private const int PruneAbove = 10_000;

    private readonly ConcurrentDictionary<(int AccountId, string Address), Entry> _entries = new();

    private sealed record Entry(int Failures, DateTime LastFailure, DateTime? LockedUntil);

    /// <summary>When the account stops being locked for this address, or null if it isn't.</summary>
    public DateTime? LockedUntil(int accountId, string address, DateTime nowUtc) =>
        _entries.TryGetValue((accountId, address), out var entry) && entry.LockedUntil > nowUtc
            ? entry.LockedUntil
            : null;

    public void RegisterFailure(int accountId, string address, DateTime nowUtc)
    {
        _entries.AddOrUpdate((accountId, address),
            _ => new Entry(1, nowUtc, null),
            (_, entry) =>
            {
                // Failures this far apart are not one guessing run.
                var failures = nowUtc - entry.LastFailure > LockoutDuration ? 1 : entry.Failures + 1;
                return failures >= PerAddressLimit
                    ? new Entry(0, nowUtc, nowUtc + LockoutDuration)
                    : new Entry(failures, nowUtc, entry.LockedUntil);
            });

        if (_entries.Count > PruneAbove)
            Prune(nowUtc);
    }

    /// <summary>A correct password from this address wipes its count.</summary>
    public void Clear(int accountId, string address) => _entries.TryRemove((accountId, address), out _);

    /// <summary>An administrator's unlock lifts the account's lock on every address.</summary>
    public void ClearAccount(int accountId)
    {
        foreach (var key in _entries.Keys.Where(k => k.AccountId == accountId).ToList())
            _entries.TryRemove(key, out _);
    }

    private void Prune(DateTime nowUtc)
    {
        foreach (var (key, entry) in _entries)
            if (nowUtc - entry.LastFailure > LockoutDuration && !(entry.LockedUntil > nowUtc))
                _entries.TryRemove(key, out _);
    }
}
