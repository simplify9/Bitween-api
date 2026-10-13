using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.NativeAdapters;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services;

/// <summary>
/// Where a receiver's last good run got to, kept for it between runs, per subscription: for any
/// receiver that keeps a bookmark - a packaged one declaring the <c>bitween-bookmark</c> contract, or
/// a built-in one that is <see cref="IKeepsBookmark"/>.
/// </summary>
/// <remarks>
/// <para>
/// A receiver can't keep it itself: each run is a new process, and the next one may be on another
/// node. So Bitween hands it the bookmark as <see cref="StartupKey"/> and asks for the new one at the
/// end of a run, the way the resident runtime already holds a polling database receiver's cursor.
/// </para>
/// <para>
/// Kept in the adapter state table, under the receiver's id and the subscription, so a subscription
/// switched to another receiver starts that one afresh, and two subscriptions never share one.
/// </para>
/// </remarks>
public class ReceiverBookmarks(BitweenDbContext dbContext, AdapterCatalog catalog, NativeAdapterDiscoveryService nativeAdapters)
{
    public const string Contract = "bitween-bookmark";
    public const string Method = nameof(IKeepsBookmark.Bookmark);

    /// <summary>The setting a receiver reads its bookmark from, by the double-underscore convention of Bitween's own values.</summary>
    public const string StartupKey = "__bookmark__";

    const string Name = "bookmark";

    static string InstanceKey(int subscriptionId) => $"subscription:{subscriptionId}";

    static string AdapterIdOf(string receiverRef) =>
        (NativeAdapterDiscoveryService.IsNative(receiverRef) ? receiverRef : AdapterCatalogPaths.Split(receiverRef).AdapterId).ToLowerInvariant();

    /// <summary>Whether the receiver keeps a bookmark: built in and <see cref="IKeepsBookmark"/>, or a package declaring the contract.</summary>
    public async Task<bool> KeptByAsync(string receiverRef)
    {
        if (string.IsNullOrWhiteSpace(receiverRef)) return false;
        if (NativeAdapterDiscoveryService.IsNative(receiverRef))
            return nativeAdapters.NativeReceiverType(receiverRef)?.IsAssignableTo(typeof(IKeepsBookmark)) == true;
        var manifest = await catalog.ManifestOf(receiverRef);
        return manifest?.Contracts?.Any(c => string.Equals(c.Key, Contract, StringComparison.OrdinalIgnoreCase) && c.Value >= 1) == true;
    }

    public async Task<AdapterState> GetAsync(int subscriptionId, string receiverRef)
    {
        var adapterId = AdapterIdOf(receiverRef);
        var key = InstanceKey(subscriptionId);
        return await dbContext.Set<AdapterState>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.AdapterId == adapterId && s.InstanceKey == key && s.Name == Name);
    }

    /// <summary>Keeps <paramref name="value"/> for the next run; saved with the caller's next SaveChanges.</summary>
    public async Task SetAsync(int subscriptionId, string receiverRef, string value)
    {
        if (value.Length > AdapterState.MaxValueLength)
            throw new BitweenException($"The receiver's bookmark is {value.Length} characters, and the most kept is {AdapterState.MaxValueLength}. " +
                                       "A bookmark says where a run got to, not what it took.");
        var adapterId = AdapterIdOf(receiverRef);
        var key = InstanceKey(subscriptionId);
        var row = await dbContext.Set<AdapterState>().SingleOrDefaultAsync(s => s.AdapterId == adapterId && s.InstanceKey == key && s.Name == Name);
        if (row == null)
            dbContext.Add(new AdapterState { AdapterId = adapterId, InstanceKey = key, Name = Name, Value = value, UpdatedOn = DateTime.UtcNow });
        else if (row.Value != value)
        {
            row.Value = value;
            row.UpdatedOn = DateTime.UtcNow;
        }
    }

    /// <summary>Forgets it, so the next run starts from the beginning. Whatever the receiver, past or present.</summary>
    public Task<int> ForgetAsync(int subscriptionId)
    {
        var key = InstanceKey(subscriptionId);
        return dbContext.Set<AdapterState>().Where(s => s.InstanceKey == key && s.Name == Name).ExecuteDeleteAsync();
    }
}
