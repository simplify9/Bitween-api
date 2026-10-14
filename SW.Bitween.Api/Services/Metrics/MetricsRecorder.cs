using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.Services.Cluster;

namespace SW.Bitween.Services.Metrics;

public enum ExchangeOutcome { Succeeded, BadResponse, Failed }

/// <summary>Where in its run a failed exchange failed.</summary>
public enum FailedAt { Elsewhere, Mapper, Handler }

public enum AdapterFailure { None, Error, Timeout, LimitStop, Crash }

/// <summary>
/// Counts what this node does, in memory, as it does it: exchanges as they finish, adapter calls as
/// they return, gateway refusals as they are made. Each is a dictionary lookup and a few additions;
/// <see cref="MetricsFlusher"/> takes the counts once a minute and adds them to the hourly rows.
/// </summary>
/// <remarks>
/// Counting takes a shared lock and draining an exclusive one, so a count made while a flush swaps
/// the tables lands in one or the other and is never lost. If a node stops without its last flush
/// (killed, not stopped), the last minute of its counts is lost: metrics, not records.
/// </remarks>
public sealed class MetricsRecorder
{
    /// <summary>This node, as its rows are written: the heartbeat's name, cut to the column.</summary>
    public static string Node { get; } = NodeHeartbeat.NodeName.Length <= 100 ? NodeHeartbeat.NodeName : NodeHeartbeat.NodeName[..100];

    readonly ReaderWriterLockSlim gate = new();
    Tables current = new();

    sealed class Tables
    {
        public readonly ConcurrentDictionary<(DateTime, int, int), ExchangeStats> Exchanges = new();
        public readonly ConcurrentDictionary<(DateTime, string, string, string, string), AdapterStats> Adapters = new();
        public readonly ConcurrentDictionary<(DateTime, string, string, string), ErrorStats> Errors = new();
    }

    /// <summary>What a flush takes away: copies, owned by the caller.</summary>
    public sealed record Snapshot(List<ExchangeStats> Exchanges, List<AdapterStats> Adapters, List<ErrorStats> Errors)
    {
        public bool IsEmpty => Exchanges.Count == 0 && Adapters.Count == 0 && Errors.Count == 0;
    }

    public static DateTime HourOf(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    public void Exchange(DateTime startedOn, int? subscriptionId, int? partnerId, ExchangeOutcome outcome,
        FailedAt failedAt, bool retry, bool manualRetry, long durationMs, long bytesIn, long bytesOut, string error)
    {
        var hour = HourOf(startedOn);
        var subscription = subscriptionId ?? ExchangeStats.None;
        gate.EnterReadLock();
        try
        {
            var row = current.Exchanges.GetOrAdd((hour, subscription, partnerId ?? ExchangeStats.None),
                k => new ExchangeStats { Hour = k.Item1, Node = Node, SubscriptionId = k.Item2, PartnerId = k.Item3 });
            lock (row)
            {
                row.Total++;
                switch (outcome)
                {
                    case ExchangeOutcome.Succeeded: row.Succeeded++; break;
                    case ExchangeOutcome.BadResponse: row.BadResponse++; break;
                    default:
                        row.Failed++;
                        if (failedAt == FailedAt.Mapper) row.FailedAtMapper++;
                        else if (failedAt == FailedAt.Handler) row.FailedAtHandler++;
                        break;
                }
                if (retry) row.Retries++;
                if (manualRetry) row.ManualRetries++;
                row.BytesIn += Math.Max(0, bytesIn);
                row.BytesOut += Math.Max(0, bytesOut);
                row.AddDuration(Math.Max(0, durationMs));
            }
            if (outcome == ExchangeOutcome.Failed && subscriptionId != null)
                CountError(ErrorStats.SubscriptionScope, subscription.ToString(), error);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    /// <summary>
    /// Adds the database commands one stage of an exchange sent to the exchange's row, without
    /// counting the exchange again; see <see cref="DbCommandTally"/>.
    /// </summary>
    public void DbCommands(DateTime startedOn, int? subscriptionId, int? partnerId, int commands)
    {
        if (commands <= 0) return;
        gate.EnterReadLock();
        try
        {
            var row = current.Exchanges.GetOrAdd(
                (HourOf(startedOn), subscriptionId ?? ExchangeStats.None, partnerId ?? ExchangeStats.None),
                k => new ExchangeStats { Hour = k.Item1, Node = Node, SubscriptionId = k.Item2, PartnerId = k.Item3 });
            lock (row) row.DbCommands += commands;
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    /// <summary>
    /// Adds the payload parses one stage of an exchange made to the exchange's row, without counting
    /// the exchange again. A stage that parsed nothing still writes its 0, so the column reads as
    /// counted rather than unknown.
    /// </summary>
    public void PayloadParses(DateTime startedOn, int? subscriptionId, int? partnerId, int parses)
    {
        if (parses < 0) return;
        gate.EnterReadLock();
        try
        {
            var row = current.Exchanges.GetOrAdd(
                (HourOf(startedOn), subscriptionId ?? ExchangeStats.None, partnerId ?? ExchangeStats.None),
                k => new ExchangeStats { Hour = k.Item1, Node = Node, SubscriptionId = k.Item2, PartnerId = k.Item3 });
            lock (row) row.PayloadParses = (row.PayloadParses ?? 0) + parses;
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public void AdapterCall(string adapterId, string version, string role, string runtime, long durationMs,
        AdapterFailure failure, string error)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) return;
        // Lower case throughout: MySQL compares keys without regard to case, and two spellings of
        // one id would otherwise be two rows here and one clashing row there.
        adapterId = Cut(adapterId.ToLowerInvariant(), 200);
        var hour = HourOf(DateTime.UtcNow);
        gate.EnterReadLock();
        try
        {
            var row = current.Adapters.GetOrAdd((hour, adapterId, Cut((version ?? "").ToLowerInvariant(), 50), role, runtime),
                k => new AdapterStats { Hour = k.Item1, Node = Node, AdapterId = k.Item2, Version = k.Item3, Role = k.Item4, Runtime = k.Item5 });
            lock (row)
            {
                row.Calls++;
                if (failure != AdapterFailure.None) row.Failures++;
                if (failure == AdapterFailure.Timeout) row.Timeouts++;
                if (failure == AdapterFailure.LimitStop) row.LimitStops++;
                if (failure == AdapterFailure.Crash) row.Crashes++;
                row.AddDuration(Math.Max(0, durationMs));
            }
            if (failure != AdapterFailure.None) CountError(ErrorStats.AdapterScope, adapterId, error);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public void Refusal(string gateway, string reason)
    {
        gate.EnterReadLock();
        try
        {
            CountError(ErrorStats.GatewayScope, Cut(string.IsNullOrWhiteSpace(gateway) ? "(unknown)" : gateway.ToLowerInvariant(), 200), reason);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    // Called with the shared lock held.
    void CountError(string scope, string key, string message)
    {
        var fingerprint = ErrorFingerprint.Of(message);
        var row = current.Errors.GetOrAdd((HourOf(DateTime.UtcNow), scope, key, fingerprint),
            k => new ErrorStats { Hour = k.Item1, Node = Node, Scope = k.Item2, ScopeKey = k.Item3, Fingerprint = k.Item4 });
        lock (row)
        {
            row.Count++;
            row.Sample = ErrorFingerprint.Sample(message);
            row.LastSeenOn = DateTime.UtcNow;
        }
    }

    /// <summary>Takes everything counted so far, leaving empty tables to count into.</summary>
    public Snapshot Drain()
    {
        Tables taken;
        gate.EnterWriteLock();
        try
        {
            taken = current;
            current = new Tables();
        }
        finally
        {
            gate.ExitWriteLock();
        }
        return new Snapshot(taken.Exchanges.Values.ToList(), taken.Adapters.Values.ToList(), taken.Errors.Values.ToList());
    }

    /// <summary>
    /// Puts back counts a flush couldn't write, to go with the next one. Older than two days, they
    /// are let go: a database that has been unreachable that long has bigger news than a chart.
    /// </summary>
    public void Restore(Snapshot snapshot)
    {
        var keep = HourOf(DateTime.UtcNow).AddDays(-2);
        gate.EnterReadLock();
        try
        {
            foreach (var s in snapshot.Exchanges.Where(s => s.Hour >= keep))
            {
                var row = current.Exchanges.GetOrAdd((s.Hour, s.SubscriptionId, s.PartnerId),
                    k => new ExchangeStats { Hour = k.Item1, Node = Node, SubscriptionId = k.Item2, PartnerId = k.Item3 });
                lock (row) row.Add(s);
            }
            foreach (var s in snapshot.Adapters.Where(s => s.Hour >= keep))
            {
                var row = current.Adapters.GetOrAdd((s.Hour, s.AdapterId, s.Version, s.Role, s.Runtime),
                    k => new AdapterStats { Hour = k.Item1, Node = Node, AdapterId = k.Item2, Version = k.Item3, Role = k.Item4, Runtime = k.Item5 });
                lock (row) row.Add(s);
            }
            foreach (var s in snapshot.Errors.Where(s => s.Hour >= keep))
            {
                var row = current.Errors.GetOrAdd((s.Hour, s.Scope, s.ScopeKey, s.Fingerprint),
                    k => new ErrorStats { Hour = k.Item1, Node = Node, Scope = k.Item2, ScopeKey = k.Item3, Fingerprint = k.Item4 });
                lock (row)
                {
                    row.Count += s.Count;
                    row.Sample ??= s.Sample;
                    if (s.LastSeenOn > row.LastSeenOn) row.LastSeenOn = s.LastSeenOn;
                }
            }
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}
