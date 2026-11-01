using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SW.Bitween.Services.Background;

/// <summary>
/// The background request running on this flow, if one is: how long its database commands may take,
/// what stops them, and where work that counts its steps says how far it has got.
/// </summary>
/// <remarks>
/// <para>
/// A request someone is waiting on keeps the connection's own limit, thirty seconds by default. A
/// background request has nobody waiting, so its commands may use whatever is left of its own limit
/// instead; and stopping it cancels the command the database is running, rather than only the wait
/// for it. Opened like <see cref="Metrics.DbCommandTally"/>, and read by
/// <see cref="BackgroundRequestCommands"/>.
/// </para>
/// <para>
/// Work that reports <see cref="Progress"/> - a bulk retry, an export - is different in two ways. Each
/// report moves its limit on, since work still making progress isn't stuck. And its owner's cancel
/// doesn't stop it mid-step: it sees <see cref="CancelRequested"/> between steps and ends there, so
/// what it has done is saved and said.
/// </para>
/// </remarks>
public sealed class BackgroundRequestScope
{
    static readonly AsyncLocal<BackgroundRequestScope> current = new();

    readonly ConcurrentDictionary<DbCommand, byte> running = new();
    readonly Action<int, int, string> progressed;

    BackgroundRequestScope(DateTime deadline, CancellationToken stopped, CancellationToken cancelRequested,
        Action<int, int, string> progressed)
    {
        Deadline = deadline;
        Stopped = stopped;
        CancelRequested = cancelRequested;
        this.progressed = progressed;
        stopped.Register(CancelRunning);
    }

    /// <summary>The background request running on this flow, if one is.</summary>
    public static BackgroundRequestScope Current => current.Value;

    /// <summary>
    /// Makes what runs on this flow from here on a background request's. Called at the top of an
    /// async method, it covers that method and everything it awaits, and ends with it.
    /// </summary>
    public static BackgroundRequestScope Open(DateTime deadline, CancellationToken stopped,
        CancellationToken cancelRequested = default, Action<int, int, string> progressed = null)
    {
        var scope = new BackgroundRequestScope(deadline, stopped, cancelRequested, progressed);
        current.Value = scope;
        return scope;
    }

    public DateTime Deadline { get; private set; }

    /// <summary>Stops it where it is: once the deadline passes, or its owner cancels work that doesn't count its steps.</summary>
    public CancellationToken Stopped { get; }

    /// <summary>Its owner cancelled it. Work that counts its steps checks this between them, and ends there.</summary>
    public CancellationToken CancelRequested { get; }

    /// <summary>Whether it has said how far it got, which makes a cancel wait for the step it's on.</summary>
    public bool Counts { get; private set; }

    /// <summary>
    /// Says how far it has got, and that it is getting somewhere: the limit starts again from now.
    /// Cheap enough to call per step; it is passed on at most once a second.
    /// </summary>
    public void Progress(int done, int total, string summary = null)
    {
        Counts = true;
        Deadline = DateTime.UtcNow + BackgroundRequests.Limit;
        progressed?.Invoke(done, total, summary);
    }

    internal void Starting(DbCommand command)
    {
        Stopped.ThrowIfCancellationRequested();
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling((Deadline - DateTime.UtcNow).TotalSeconds));
        running.TryAdd(command, 0);
        // A stop landing between the check and the add found nothing to cancel.
        if (Stopped.IsCancellationRequested) Cancel(command);
    }

    internal void Ended(DbCommand command) => running.TryRemove(command, out _);

    void CancelRunning()
    {
        foreach (var command in running.Keys) Cancel(command);
    }

    static void Cancel(DbCommand command)
    {
        try
        {
            command.Cancel();
        }
        catch
        {
            // Already finished, or the provider can't; the deadline still applies.
        }
    }
}

/// <summary>
/// Applies the background request open on the calling flow to each command EF sends; nothing when
/// none is open. A command that reads stays covered until its reader is disposed, since a large read
/// is still running after the command has returned.
/// </summary>
public sealed class BackgroundRequestCommands : DbCommandInterceptor
{
    public static readonly BackgroundRequestCommands Instance = new();

    BackgroundRequestCommands()
    {
    }

    static void Starting(DbCommand command) => BackgroundRequestScope.Current?.Starting(command);
    static void Ended(DbCommand command) => BackgroundRequestScope.Current?.Ended(command);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Starting(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Starting(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData eventData,
        InterceptionResult result)
    {
        Ended(command);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Starting(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Starting(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Ended(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        Ended(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Starting(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Starting(command);
        return ValueTask.FromResult(result);
    }

    public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result)
    {
        Ended(command);
        return result;
    }

    public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object result, CancellationToken cancellationToken = default)
    {
        Ended(command);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Ended(command);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Ended(command);
        return Task.CompletedTask;
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) => Ended(command);

    public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Ended(command);
        return Task.CompletedTask;
    }
}
