using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace SW.Bitween.NativeAdapters;

public interface IDynamicHttpProxy
{
    HttpClient GetClient(string fullUrl);
}
public class DynamicHttpProxy(IHttpClientFactory httpClientFactory) : BackgroundService, IDynamicHttpProxy
{
    /// <summary>The named client every adapter call goes through; it carries the address guard.</summary>
    public const string ClientName = "bitween-adapters";

    private readonly ConcurrentDictionary<string, HttpClient> _cache = new();
    private readonly Channel<string> _usageChannel = Channel.CreateUnbounded<string>();
    
    // Internal LRU state (only accessed by the background thread)
    private readonly LinkedList<string> _lruList = new();
    private const int MaxCapacity = 200;

    public HttpClient GetClient(string fullUrl)
    {
        var uri = new Uri(fullUrl);
        string origin = $"{uri.Scheme}://{uri.Authority}";

        // Fast path: No lock, thread-safe read
        var client = _cache.GetOrAdd(origin, key => {
            var newClient = httpClientFactory.CreateClient(ClientName);
            newClient.BaseAddress = new Uri(key);
            return newClient;
        });

        // Notify background worker of usage (non-blocking)
        _usageChannel.Writer.TryWrite(origin);

        return client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The Scavenger Loop
        await foreach (var origin in _usageChannel.Reader.ReadAllAsync(stoppingToken))
        {
            // Maintenance logic happens here, off the hot path
            UpdateLru(origin);
        }
    }

    private void UpdateLru(string origin)
    {
        // Reorder list
        _lruList.Remove(origin);
        _lruList.AddFirst(origin);

        // Prune if we went over capacity
        while (_cache.Count > MaxCapacity && _lruList.Last is { } tail)
        {
            var oldest = tail.Value;
            _lruList.RemoveLast();
            _cache.TryRemove(oldest, out _);
        }
    }
}