using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SW.Bitween;

/// <summary>
/// Counts too slow to answer within a request, finished on the server instead and kept for the page
/// that asks again: the Exchanges page's status counts, its chart, and with them the list's total.
/// "Processing" across millions of exchanges, or a busy day's chart, takes longer than a request's 30
/// seconds. The page shows what it has and asks again until the rest is there.
/// </summary>
/// <remarks>
/// Kept in memory, on the node that counted. In a cluster each node counts a slow view once for itself,
/// which is still once per node rather than once per refresh.
/// </remarks>
public sealed class SlowCounts(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime,
    ILogger<SlowCounts> logger)
{
    /// <summary>
    /// How long an answer stands before it is counted again. It is still given out while the new one is
    /// worked out. A failed count is tried again after as long, and gives out nothing meanwhile.
    /// </summary>
    internal static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(5);

    /// <summary>An answer nobody has asked for in this long is dropped.</summary>
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A count whose turn comes when nobody has asked for it in this long is left uncounted: its page was
    /// closed or moved to other filters. A page still waiting asks at least every minute.
    /// </summary>
    private static readonly TimeSpan StillWanted = TimeSpan.FromMinutes(2);

    /// <summary>The time one count gets on the database, where a request's commands get 30 seconds.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many count at once on a node. Each is a long read of the largest tables, and a burst of slow
    /// views mustn't take the database from the exchanges themselves; the rest wait their turn.
    /// </summary>
    private const int AtOnce = 2;

    private readonly ConcurrentDictionary<string, Entry> entries = new();
    private readonly SemaphoreSlim places = new(AtOnce, AtOnce);

    /// <summary>
    /// Whether <paramref name="key"/> is one counted here, given out as <paramref name="answer"/> - the
    /// default until the first count of it is in. Counted again in the background once its answer is
    /// older than <see cref="KeepFor"/>.
    /// </summary>
    public bool TryGet<T>(string key, Func<BitweenDbContext, CancellationToken, Task<T>> count, out T answer)
    {
        answer = default;
        if (!entries.TryGetValue(key, out var entry))
            return false;

        if (entry.Last is { } last)
            answer = (T)last.Value;
        Start(key, count);
        return true;
    }

    /// <summary>
    /// Counts <paramref name="key"/> in the background, for a later <see cref="TryGet{T}"/> to find. Does
    /// nothing while it is already being counted, or was within <see cref="KeepFor"/>.
    /// </summary>
    public void Start<T>(string key, Func<BitweenDbContext, CancellationToken, Task<T>> count)
    {
        var now = DateTime.UtcNow;
        var entry = entries.GetOrAdd(key, _ => new Entry());
        entry.AskedOn = now;
        Forget(now);

        if (now - entry.TriedOn < KeepFor || Interlocked.Exchange(ref entry.Counting, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            var stopping = lifetime.ApplicationStopping;
            try
            {
                await places.WaitAsync(stopping);
                try
                {
                    // Left without trying, so it starts again as soon as its page is back.
                    if (DateTime.UtcNow - entry.AskedOn > StillWanted)
                        return;

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
                    dbContext.Database.SetCommandTimeout(CommandTimeout);
                    entry.Last = new Kept(await count(dbContext, stopping));
                    entry.TriedOn = DateTime.UtcNow;
                }
                finally
                {
                    places.Release();
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // The last answer can't be brought up to date, so it isn't given out as if it were.
                entry.Last = null;
                entry.TriedOn = DateTime.UtcNow;
                logger.LogWarning(ex, "A count left to finish in the background failed. It is tried again in {Minutes} minutes.",
                    KeepFor.TotalMinutes);
            }
            finally
            {
                Interlocked.Exchange(ref entry.Counting, 0);
            }
        });
    }

    private void Forget(DateTime now)
    {
        foreach (var (key, entry) in entries)
            if (entry.Counting == 0 && now - entry.AskedOn > ForgetAfter)
                entries.TryRemove(key, out _);
    }

    private sealed class Entry
    {
        public volatile Kept Last;
        public int Counting;
        public DateTime TriedOn;
        public DateTime AskedOn;
    }

    private sealed record Kept(object Value);
}
