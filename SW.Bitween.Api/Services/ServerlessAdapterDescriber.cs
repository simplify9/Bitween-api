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
/// of key names. So the answer is remembered for as long as the build it came from is the one that
/// runs, one ask is shared by everyone who wants it at that moment, and only so many adapters may be
/// running at once.
/// </para>
/// <para>
/// A singleton, and it has to be. All three of those only work between requests: a per-request copy
/// would coalesce nothing, limit nothing, and remember nothing past the request that filled it.
/// </para>
/// </remarks>
public class ServerlessAdapterDescriber(
    IServiceScopeFactory scopeFactory,
    IMemoryCache memoryCache,
    SW.Serverless.AdapterInstaller adapterInstaller)
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
        var build = await BuildOf(adapterId);
        if (memoryCache.TryGetValue(CacheKey(adapterId), out Answer cached) && cached.Build == build)
            return cached.Values;

        // Everyone who wants this adapter while it is being asked waits on the one ask, rather than
        // starting an identical child process of their own. Without this the cache does not help
        // the case it most needs to - a cold start, when every request arrives at once.
        var key = $"{adapterId}@{build}";
        var asking = _asking.GetOrAdd(key, _ =>
            new Lazy<Task<IDictionary<string, StartupValue>>>(() => Ask(adapterId, build),
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
                key, asking));
        }
    }

    /// <summary>
    /// Which build of the adapter a start would run: its package's hash, as the installer has it.
    /// A storage read at most, where asking is a child process.
    /// </summary>
    private async Task<string> BuildOf(string adapterId)
    {
        try
        {
            return (await adapterInstaller.GetMetadataAsync(adapterId)).Hash;
        }
        catch (KeyNotFoundException ex)
        {
            throw Incomplete(adapterId, ex);
        }
    }

    private async Task<IDictionary<string, StartupValue>> Ask(string adapterId, string build)
    {
        await _starts.WaitAsync();
        try
        {
            // The queue may have been long enough for someone else to have answered this already.
            if (memoryCache.TryGetValue(CacheKey(adapterId), out Answer cached) && cached.Build == build)
                return cached.Values;

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
                throw Incomplete(adapterId, ex);
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
            // Held for as long as this build is the one that runs, rather than the five minutes it
            // was, after which every adapter was started again for an answer that had not changed:
            // the first read of a subscription after that took a second per adapter. A re-uploaded
            // adapter has a new hash, which the installer has once its metadata cache runs out -
            // when ServerlessService starts booting the new build too - and is asked again then. A
            // day without anyone asking lets the answer go.
            return memoryCache.Set(CacheKey(adapterId), new Answer(build, shared),
                new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(1) }).Values;
        }
        finally
        {
            _starts.Release();
        }
    }

    private static BitweenException Incomplete(string adapterId, KeyNotFoundException ex) => new(
        $"Adapter '{adapterId}' metadata is incomplete or the adapter package is not installed. " +
        $"Missing metadata key: {ex.Message}", ex);

    /// <summary>What one build of an adapter said it expects.</summary>
    private sealed record Answer(string Build, IDictionary<string, StartupValue> Values);

    private static string CacheKey(string adapterId) =>
        $"{CacheKeyPrefix}.{adapterId.ToLowerInvariant()}";
}
