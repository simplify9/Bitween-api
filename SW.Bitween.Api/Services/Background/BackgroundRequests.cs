using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SW.Bitween.Domain;
using SW.Bitween.Services.Cluster;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Background;

/// <summary>
/// Runs the work the admin UI asks to carry on - prod's list to bring from, a bulk retry, a zip of
/// exchange files - and keeps going with whatever outlasts the wait the UI asked for: that becomes a
/// <see cref="BackgroundRequest"/>, whose answer is kept for the UI to collect, and whose owner is told
/// when it changes - by SignalR, on whichever node holds their connection.
/// </summary>
/// <remarks>
/// <para>
/// A request starts the moment it arrives, so one that answers in time is answered exactly as it would
/// have been. What it may not do is run in the background beyond <see cref="MaxRunning"/> on a node, or
/// <see cref="MaxPerOwner"/> of one person's: they are long database work, on the database exchanges are
/// processed on. A read that would go past either is stopped and asked again once a place frees up; a
/// write can't be asked twice, so it goes on regardless.
/// </para>
/// <para>
/// A singleton, and has to be: it holds the places, and the requests running here.
/// </para>
/// </remarks>
public sealed class BackgroundRequests(IServiceScopeFactory scopes, IHubContext<BackgroundRequestHub> hub,
    ICloudFilesService cloudFiles, ILogger<BackgroundRequests> logger)
{
    /// <summary>The longest a background request may go without saying it got further.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    /// <summary>How long an answer is kept for its owner to collect.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(30);

    public const int MaxRunning = 10;
    public const int MaxPerOwner = 3;

    /// <summary>Answers up to this size are kept in the database; larger ones, and files, in storage.</summary>
    public const int KeptInline = 1024 * 1024;

    /// <summary>Where answers kept in storage go, each under its request's id.</summary>
    public const string StoragePrefix = "background-requests/";

    static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(1);

    /// <summary>This instance, among those a change is broadcast to; unique even for two in one process.</summary>
    public string Node { get; } = $"{NodeHeartbeat.NodeName}/{Guid.NewGuid():N}";

    readonly SemaphoreSlim places = new(MaxRunning, MaxRunning);
    readonly ConcurrentDictionary<string, SemaphoreSlim> ownerPlaces = new();
    readonly ConcurrentDictionary<string, Work> background = new();

    /// <summary>Starts answering <paramref name="asked"/>, by running what follows in the pipeline.</summary>
    internal Work Start(AskedRequest asked, RequestDelegate next, string owner, string label, string page)
    {
        var work = new Work(asked, next, owner, label, page);
        // Its own flow from the start: nothing set on the request's - the context accessor's holder
        // above all, which is cleared when the request ends - may reach it.
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => RunAsync(work));
        return work;
    }

    /// <summary>
    /// Keeps <paramref name="work"/> going after its caller stopped waiting: records it, and gives it a
    /// place - or, for a read, stops it until one frees up.
    /// </summary>
    internal async Task<BackgroundRequest> KeepAsync(Work work)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await ForgetExpiredAsync(db);

            var row = new BackgroundRequest(work.Id, work.Owner, Clip(work.Asked.Described, 4000),
                Clip(work.Label, 200), Clip(work.Page, 2000), NodeHeartbeat.NodeName);
            if (TryTakePlaces(work) || !work.Asked.IsRead) row.Run();
            else work.Requeue();
            if (work.Total is { } total) row.Progress(work.Done, total, Clip(work.Summary, 500));

            background[work.Id] = work;
            db.Add(row);
            await db.SaveChangesAsync();
            work.Kept.TrySetResult(true);
            await NotifyAsync(row);
            return row;
        }
        catch (Exception ex)
        {
            // Nobody could collect it, so it must not go on.
            work.Cancel(hard: true);
            background.TryRemove(work.Id, out _);
            ReleasePlaces(work);
            work.Kept.TrySetResult(false);
            logger.LogError(ex, "Couldn't keep a background request for {Path}.", work.Asked.Described);
            throw;
        }
    }

    /// <summary>Stops a request running on this node; false when it isn't running here.</summary>
    public bool CancelHere(string id)
    {
        if (!background.TryGetValue(id, out var work)) return false;
        work.Cancel();
        return true;
    }

    /// <summary>
    /// Stops one of <paramref name="owner"/>'s requests, wherever it runs. False when there is no such
    /// request of theirs.
    /// </summary>
    /// <remarks>
    /// Work that counts its steps ends itself, at the next one, and says how far it got; anything else
    /// ends here and now.
    /// </remarks>
    public async Task<bool> CancelAsync(BitweenDbContext db, string owner, string id)
    {
        var row = await db.Set<BackgroundRequest>().FirstOrDefaultAsync(r => r.Id == id && r.Owner == owner);
        if (row == null) return false;
        if (row.Finished) return true;

        if (!CancelHere(id)) await BroadcastAsync(new BackgroundRequestCancel { Id = id });
        if (row.Total != null) return true;

        row.End(BackgroundRequestStatus.Cancelled);
        await db.SaveChangesAsync();
        await NotifyAsync(row);
        return true;
    }

    /// <summary>
    /// Ends whichever of these is still running on a node that has gone, or has restarted since it
    /// started it: those will never finish.
    /// </summary>
    public static async Task EndStoppedAsync(BitweenDbContext db, IReadOnlyCollection<BackgroundRequest> rows)
    {
        var unfinished = rows.Where(r => !r.Finished).ToList();
        if (unfinished.Count == 0) return;

        var names = unfinished.Select(r => r.Node).Distinct().ToList();
        var nodes = await db.Set<Domain.Cluster.ClusterNode>().AsNoTracking()
            .Where(n => names.Contains(n.Id)).ToDictionaryAsync(n => n.Id);
        var now = DateTime.UtcNow;
        var ended = false;
        foreach (var row in unfinished)
        {
            var stopped = nodes.TryGetValue(row.Node, out var node)
                ? node.LastSeenOn < now - NodeHeartbeat.GoneAfter || node.StartedOn > row.StartedOn
                // A node writes its first beat as it starts; a request that new gets the same grace.
                : row.StartedOn < now - NodeHeartbeat.GoneAfter;
            if (!stopped) continue;
            row.End(BackgroundRequestStatus.Stopped);
            ended = true;
        }

        if (ended) await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes what is no longer kept - finished longer ago than <see cref="KeptFor"/>, or started more
    /// than a day ago - with the answers it kept in storage.
    /// </summary>
    async Task ForgetExpiredAsync(BitweenDbContext db)
    {
        var now = DateTime.UtcNow;
        var expired = db.Set<BackgroundRequest>()
            .Where(r => r.StartedOn < now.AddDays(-1) || (r.FinishedOn != null && r.FinishedOn < now - KeptFor));
        foreach (var key in await expired.Where(r => r.ResultKey != null).Select(r => r.ResultKey).ToListAsync())
        {
            try
            {
                await cloudFiles.DeleteAsync(key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't delete a background request's answer, {Key}.", key);
            }
        }

        await expired.ExecuteDeleteAsync();
    }

    async Task RunAsync(Work work)
    {
        Answer answer = null;
        try
        {
            answer = await AttemptAsync(work, work.FirstAttempt);
            if (work.TryAnswerHere())
            {
                work.AnsweredHere.TrySetResult(answer);
                answer = null;
                return;
            }

            if (!await work.Kept.Task) return;
            try
            {
                // A read stopped for want of a place rather than finished: asked again once one frees up.
                if (answer.Kind == AnswerKind.Requeued)
                {
                    try
                    {
                        await TakePlacesAsync(work);
                    }
                    catch (OperationCanceledException)
                    {
                        await SaveAsync(work, Answer.Cancelled);
                        return;
                    }

                    await SetRunningAsync(work);
                    using var attempt = new CancellationTokenSource();
                    answer = await AttemptAsync(work, attempt);
                }

                await SaveAsync(work, answer);
            }
            finally
            {
                ReleasePlaces(work);
                background.TryRemove(work.Id, out _);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A background request for {Path} failed.", work.Asked.Described);
            if (!work.AnsweredHere.TrySetException(ex) && work.Kept.Task.IsCompletedSuccessfully && work.Kept.Task.Result)
            {
                try
                {
                    await SaveAsync(work, Answer.Failed);
                }
                catch (Exception saving)
                {
                    logger.LogError(saving, "Couldn't record that a background request failed.");
                }
            }
        }
        finally
        {
            if (answer?.Body != null) await answer.Body.DisposeAsync();
        }
    }

    /// <summary>
    /// Asks the request again, in a scope of its own, with <see cref="Limit"/> to get anywhere and
    /// <paramref name="stop"/> able to end it sooner.
    /// </summary>
    async Task<Answer> AttemptAsync(Work work, CancellationTokenSource stop)
    {
        work.Attempt = stop;
        stop.CancelAfter(Limit);
        BackgroundRequestScope.Open(DateTime.UtcNow + Limit, stop.Token, work.CancelRequested,
            (done, total, summary) => Progressed(work, stop, done, total, summary));

        await using var scope = scopes.CreateAsyncScope();
        // On disk rather than in memory: an answer can be a zip of hundreds of exchanges' files.
        var body = new FileStream(Path.Combine(Path.GetTempPath(), $"bitween-background-{work.Id}-{work.Attempts + 1}"),
            FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var context = work.Asked.NewContext(scope.ServiceProvider, body, stop.Token, ++work.Attempts);
        var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
        if (accessor != null) accessor.HttpContext = context;

        try
        {
            await work.Next(context);
            await context.Response.CompleteAsync();
        }
        catch (Exception) when (stop.IsCancellationRequested)
        {
            await body.DisposeAsync();
            return work.CancelRequested.IsCancellationRequested ? Answer.Cancelled
                : work.Requeued ? Answer.Requeued
                : Answer.TimedOut;
        }
        catch (Exception ex)
        {
            await body.DisposeAsync();
            logger.LogError(ex, "A background request for {Path} threw.", work.Asked.Described);
            return Answer.Failed;
        }
        finally
        {
            if (accessor != null) accessor.HttpContext = null;
        }

        body.Position = 0;
        // The answer the web host would have rewritten on its way out; see Startup.
        var status = context.Response.StatusCode == StatusCodes.Status401Unauthorized &&
                     context.User.Identity?.IsAuthenticated == true
            ? StatusCodes.Status403Forbidden
            : context.Response.StatusCode;
        return new Answer(AnswerKind.Answered, status, context.Response.ContentType,
            context.Response.Headers[HeaderNames.ContentDisposition].FirstOrDefault(), context.Response.Headers, body);
    }

    /// <summary>
    /// Work said how far it got: it is still going, so its limit starts again; and its owner hears,
    /// once a second at most.
    /// </summary>
    void Progressed(Work work, CancellationTokenSource stop, int done, int total, string summary)
    {
        work.Counts = true;
        try
        {
            stop.CancelAfter(Limit);
        }
        catch (ObjectDisposedException)
        {
            // The attempt it was for is over.
        }

        work.Done = done;
        work.Total = total;
        work.Summary = summary;
        var now = DateTime.UtcNow;
        if (now - work.ToldOn < ProgressEvery || !work.Kept.Task.IsCompletedSuccessfully || !work.Kept.Task.Result)
            return;
        work.ToldOn = now;

        // Off the work's own flow, so it neither waits for this nor has its limits applied to it.
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                    var row = await db.Set<BackgroundRequest>().FindAsync(work.Id);
                    if (row == null || row.Finished) return;
                    row.Progress(done, total, Clip(summary, 500));
                    await db.SaveChangesAsync();
                    await NotifyAsync(row);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Couldn't record how far a background request got.");
                }
            });
    }

    async Task SaveAsync(Work work, Answer answer)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<BackgroundRequest>().FindAsync(work.Id);
        // Gone, or ended meanwhile: nothing to tell anyone.
        if (row == null || row.Finished) return;
        if (work.Total is { } total) row.Progress(work.Done, total, Clip(work.Summary, 500));

        switch (answer.Kind)
        {
            case AnswerKind.Answered:
                var file = answer.Disposition != null || answer.Body.Length > KeptInline;
                string key = null;
                byte[] inline = null;
                if (file)
                {
                    key = StoragePrefix + work.Id;
                    await cloudFiles.WriteAsync(answer.Body, new WriteFileSettings
                    {
                        Key = key,
                        ContentType = answer.ContentType ?? "application/octet-stream"
                    });
                }
                else
                {
                    using var copy = new MemoryStream();
                    await answer.Body.CopyToAsync(copy);
                    inline = copy.ToArray();
                }

                row.Answer(answer.Status, answer.ContentType, answer.Disposition, inline, key,
                    cancelled: work.CancelRequested.IsCancellationRequested);
                break;
            case AnswerKind.Failed:
                row.Answer(StatusCodes.Status500InternalServerError, "application/json", null,
                    Problem("BACKGROUND_FAILED", "It failed while running. The log of the node that ran it says why."), null);
                break;
            case AnswerKind.TimedOut:
                row.End(BackgroundRequestStatus.TimedOut);
                break;
            default:
                row.End(BackgroundRequestStatus.Cancelled);
                break;
        }

        await db.SaveChangesAsync();
        await NotifyAsync(row);
    }

    async Task SetRunningAsync(Work work)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<BackgroundRequest>().FindAsync(work.Id);
        if (row == null || row.Finished) return;
        row.Run();
        await db.SaveChangesAsync();
        await NotifyAsync(row);
    }

    /// <summary>A body as the UI reads a refusal: the code, with what to tell the person.</summary>
    internal static byte[] Problem(string code, string message) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string[]> { [code] = [message] }));

    bool TryTakePlaces(Work work)
    {
        var owners = ownerPlaces.GetOrAdd(work.Owner, _ => new SemaphoreSlim(MaxPerOwner, MaxPerOwner));
        if (!owners.Wait(0)) return false;
        if (places.Wait(0))
        {
            work.HoldsPlaces = true;
            return true;
        }

        owners.Release();
        return false;
    }

    async Task TakePlacesAsync(Work work)
    {
        var owners = ownerPlaces.GetOrAdd(work.Owner, _ => new SemaphoreSlim(MaxPerOwner, MaxPerOwner));
        await owners.WaitAsync(work.CancelRequested);
        try
        {
            await places.WaitAsync(work.CancelRequested);
        }
        catch
        {
            owners.Release();
            throw;
        }

        work.HoldsPlaces = true;
    }

    void ReleasePlaces(Work work)
    {
        if (!work.HoldsPlaces) return;
        work.HoldsPlaces = false;
        places.Release();
        ownerPlaces[work.Owner].Release();
    }

    /// <summary>
    /// Tells the owner's connections here, then every other node, whose listener tells theirs. Losing
    /// either loses only the push: the UI asks again when it reconnects.
    /// </summary>
    internal async Task NotifyAsync(BackgroundRequest row)
    {
        var change = BackgroundRequestChanged.Of(row, Node);
        await PushAsync(change);
        await BroadcastAsync(change);
    }

    internal async Task PushAsync(BackgroundRequestChanged change)
    {
        try
        {
            await hub.Clients.User(change.Owner).SendAsync(BackgroundRequestHub.Changed, new
            {
                id = change.Id,
                status = change.Status.ToString(),
                done = change.Done,
                total = change.Total,
                summary = change.Summary,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't tell a browser about a background request.");
        }
    }

    // Generic, not object: SW-Bus names a broadcast by the type it is called with, and a listener
    // only hears its own type's name. Called with object, every node dropped it without a word.
    async Task BroadcastAsync<TMessage>(TMessage message)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IBroadcast>().Broadcast(message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't tell the other nodes about a background request.");
        }
    }

    static string Clip(string value, int length) =>
        value == null || value.Length <= length ? value : value[..length];

    internal enum AnswerKind
    {
        Answered,
        Failed,
        Cancelled,
        TimedOut,
        Requeued,
    }

    /// <summary>What an attempt came to. <see cref="Body"/> is a temporary file, deleted when disposed.</summary>
    internal sealed record Answer(AnswerKind Kind, int Status = 0, string ContentType = null, string Disposition = null,
        IHeaderDictionary Headers = null, Stream Body = null)
    {
        public static readonly Answer Failed = new(AnswerKind.Failed);
        public static readonly Answer Cancelled = new(AnswerKind.Cancelled);
        public static readonly Answer TimedOut = new(AnswerKind.TimedOut);
        public static readonly Answer Requeued = new(AnswerKind.Requeued);
    }

    /// <summary>One request, from when it arrives until it is answered or ends.</summary>
    internal sealed class Work(AskedRequest asked, RequestDelegate next, string owner, string label, string page)
    {
        const int Waited = 0, Moved = 1, Answered = 2;
        int state;
        readonly CancellationTokenSource cancelRequested = new();

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public AskedRequest Asked { get; } = asked;
        public RequestDelegate Next { get; } = next;
        public string Owner { get; } = owner;
        public string Label { get; } = label;
        public string Page { get; } = page;
        public int Attempts { get; set; }
        public bool HoldsPlaces { get; set; }
        public bool Requeued { get; private set; }

        /// <summary>It has said how far it got, so a cancel lets it end at its next step.</summary>
        public bool Counts { get; set; }

        public int Done { get; set; }
        public int? Total { get; set; }
        public string Summary { get; set; }
        public DateTime ToldOn { get; set; }

        /// <summary>Its owner's cancel, or its caller leaving before it moved to the background.</summary>
        public CancellationToken CancelRequested => cancelRequested.Token;

        /// <summary>What stops the first attempt: a cancel, a requeue, or the limit.</summary>
        public CancellationTokenSource FirstAttempt { get; } = new();

        /// <summary>What stops the attempt under way.</summary>
        public CancellationTokenSource Attempt { get; set; }

        /// <summary>The answer, when it came while the caller still waited.</summary>
        public TaskCompletionSource<Answer> AnsweredHere { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True once its record exists, false when keeping it failed.</summary>
        public TaskCompletionSource<bool> Kept { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Claims the answer for the caller still waiting; false once it moved to the background.</summary>
        public bool TryAnswerHere() => Interlocked.CompareExchange(ref state, Answered, Waited) == Waited;

        /// <summary>Claims it for the background; false once its caller has been answered.</summary>
        public bool TryMove() => Interlocked.CompareExchange(ref state, Moved, Waited) == Waited;

        /// <summary>
        /// Stops it: where it is, unless it counts its steps - then at the next one, unless
        /// <paramref name="hard"/>.
        /// </summary>
        public void Cancel(bool hard = false)
        {
            cancelRequested.Cancel();
            if (Counts && !hard) return;
            FirstAttempt.Cancel();
            try
            {
                Attempt?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // That attempt is over.
            }
        }

        /// <summary>Stops the first attempt, to ask again once there is a place for it.</summary>
        public void Requeue()
        {
            Requeued = true;
            FirstAttempt.Cancel();
        }
    }
}
