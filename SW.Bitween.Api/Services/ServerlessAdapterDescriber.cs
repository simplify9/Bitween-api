using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Asks a published adapter what startup properties it expects, and remembers the answer.
/// </summary>
/// <remarks>
/// <para>
/// Asking is expensive out of all proportion to the answer: the package is downloaded and unzipped,
/// a <c>dotnet</c> child process is started, and the question goes over stdio - to be told a handful
/// of key names. So the answer is cached, one ask is shared by everyone who wants it at that moment,
/// and only so many adapters may be running at once.
/// </para>
/// <para>
/// A singleton, and it has to be. All three of those only work between requests: a per-request copy
/// would coalesce nothing, limit nothing, and remember nothing past the request that filled it.
/// </para>
/// </remarks>
public class ServerlessAdapterDescriber(
    IServiceScopeFactory scopeFactory,
    IMemoryCache memoryCache,
    ServerlessOptions serverlessOptions)
{
    private const string CacheKeyPrefix = "bitween.adapters.startupvalues";

    /// <summary>
    /// How many adapters may be running at once, across the whole process.
    /// <para>
    /// Each one is a child process, and the callers are not coordinated - a screen loading four
    /// kinds of adapter is four requests, and there can be a request per user on top of that. Six
    /// is what one browser was already allowed per host before any of this was batched, so the
    /// server holds no more open at a time than it used to.
    /// </para>
    /// </summary>
    private const int MaxConcurrentStarts = 6;

    private readonly SemaphoreSlim _starts = new(MaxConcurrentStarts, MaxConcurrentStarts);

    private readonly ConcurrentDictionary<string, Lazy<Task<IDictionary<string, StartupValue>>>> _asking =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drops what is remembered about an adapter, so the next ask runs it again.</summary>
    public void Forget(string adapterId) => memoryCache.Remove(CacheKey(adapterId));

    public async Task<IDictionary<string, StartupValue>> Describe(string adapterId)
    {
        if (memoryCache.TryGetValue(CacheKey(adapterId), out IDictionary<string, StartupValue> cached))
            return cached;

        // Everyone who wants this adapter while it is being asked waits on the one ask, rather than
        // starting an identical child process of their own. Without this the cache does not help
        // the case it most needs to - a cold start, when every request arrives at once.
        var asking = _asking.GetOrAdd(adapterId, id =>
            new Lazy<Task<IDictionary<string, StartupValue>>>(() => Ask(id),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await asking.Value;
        }
        finally
        {
            // Removed whether it worked or not. A failure left here would be what every later
            // request awaits, forever; a success is in the cache by now, so the next request never
            // needed this entry anyway. Matched on the instance so that a newer ask, started by
            // someone else after this one finished, is not the one taken away.
            _asking.TryRemove(new KeyValuePair<string, Lazy<Task<IDictionary<string, StartupValue>>>>(
                adapterId, asking));
        }
    }

    private async Task<IDictionary<string, StartupValue>> Ask(string adapterId)
    {
        await _starts.WaitAsync();
        try
        {
            // The queue may have been long enough for someone else to have answered this already.
            if (memoryCache.TryGetValue(CacheKey(adapterId), out IDictionary<string, StartupValue> cached))
                return cached;

            // Its own scope, disposed the moment this returns, because disposing the serverless
            // service is what quits the child process. On the request's scope instead, a caller
            // describing a whole catalogue would hold every adapter it started open until the
            // request ended, rather than one at a time.
            await using var scope = scopeFactory.CreateAsyncScope();
            var serverless = scope.ServiceProvider.GetRequiredService<IServerlessService>();

            try
            {
                await serverless.StartAsync(adapterId, null);
            }
            catch (KeyNotFoundException ex)
            {
                throw new BitweenException(
                    $"Adapter '{adapterId}' metadata is incomplete or the adapter package is not installed. " +
                    $"Missing metadata key: {ex.Message}", ex);
            }

            var startupValues = await serverless.GetExpectedStartupValues()
                                ?? new Dictionary<string, StartupValue>();

            // Read-only because one instance is now handed to every request that asks. A caller
            // that edited it in place would be editing what the next request is told the adapter
            // expects, and this way that attempt throws instead.
            IDictionary<string, StartupValue> shared =
                new ReadOnlyDictionary<string, StartupValue>(startupValues);

            // Only a successful answer is cached - a boot that failed because, say, the adapter's
            // runtime is missing locally must not stick around as "this adapter has no properties".
            //
            // Held for as long as ServerlessService already serves a stale Hash from its own
            // metadata cache. Within that window it boots the previous build of a re-uploaded
            // adapter regardless, so this adds no staleness that re-uploading did not already have.
            return memoryCache.Set(CacheKey(adapterId), shared,
                TimeSpan.FromMinutes(serverlessOptions.AdapterMetadataCacheDuration));
        }
        finally
        {
            _starts.Release();
        }
    }

    private static string CacheKey(string adapterId) =>
        $"{CacheKeyPrefix}.{adapterId.ToLowerInvariant()}";
}
