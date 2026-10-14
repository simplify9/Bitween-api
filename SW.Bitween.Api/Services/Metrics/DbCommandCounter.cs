using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// The database commands one stage of one exchange sent: its creation on a gateway call, its
/// processing, its result lane. Opened by the stage, counted by <see cref="DbCommandCounter"/>.
/// </summary>
/// <remarks>
/// The count is how the work on database round trips is measured (processing-performance.md,
/// finding 4): summed per exchange into <c>ExchangeStats.DbCommands</c>, so commands per exchange is
/// that column over <c>Total</c>, and exported as a histogram per stage.
/// </remarks>
public sealed class DbCommandTally
{
    static readonly AsyncLocal<DbCommandTally> current = new();

    readonly string stage;
    int count;
    DateTime startedOn;
    int? subscriptionId, partnerId;
    bool attributed;

    DbCommandTally(string stage) => this.stage = stage;

    /// <summary>The tally of the stage running on this flow, if one was opened.</summary>
    public static DbCommandTally Current => current.Value;

    /// <summary>
    /// Opens a tally for what runs on this flow from here on. Called at the top of an async method,
    /// it covers that method and everything it awaits, and ends with it.
    /// </summary>
    public static DbCommandTally Open(string stage)
    {
        var tally = new DbCommandTally(stage);
        current.Value = tally;
        return tally;
    }

    public int Count => Volatile.Read(ref count);

    internal void Increment() => Interlocked.Increment(ref count);

    /// <summary>Which exchange's row the count goes to, once the stage knows.</summary>
    public void AttributeTo(DateTime startedOn, int? subscriptionId, int? partnerId)
    {
        this.startedOn = startedOn;
        this.subscriptionId = subscriptionId;
        this.partnerId = partnerId;
        attributed = true;
    }

    /// <summary>
    /// Adds the count to the exchange's hourly row and the histogram. Nothing for a stage that never
    /// learned which exchange it was for, such as a gateway call refused before it found one.
    /// </summary>
    public void Record(MetricsRecorder recorder)
    {
        if (!attributed) return;
        BitweenTelemetry.DbCommands.Record(Count, new KeyValuePair<string, object>("stage", stage));
        recorder?.DbCommands(startedOn, subscriptionId, partnerId, Count);
    }
}

/// <summary>
/// Counts each command EF sends against the tally open on the calling flow. An increment per
/// command: nanoseconds against a round trip of a millisecond, and nothing when no tally is open.
/// </summary>
public sealed class DbCommandCounter : DbCommandInterceptor
{
    public static readonly DbCommandCounter Instance = new();

    DbCommandCounter()
    {
    }

    static void Count() => DbCommandTally.Current?.Increment();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }
}
