using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The server holds subscriptions, information types, notifiers, work groups, global values and
/// bus gateways in memory for ten minutes, because the message path reads them for every message
/// and cannot go to the database each time. A write therefore has to announce itself, or the
/// running system keeps acting on what it read ten minutes ago.
/// </summary>
/// <remarks>
/// Announcing means <c>BroadcastRevoke()</c>, which publishes rather than clearing directly: each
/// instance owns a private queue bound to the shared node exchange, so every instance clears its
/// own copy — including the one that handled the write. The fixture deliberately does not call
/// <c>AddBusConsume</c>, so that round trip cannot complete here and the announcement is asserted
/// at the handler instead, with a cache that records the call.
/// </remarks>
[Collection("Bitween")]
public class CacheRevocationTests
{
    private readonly BitweenFixture _fixture;

    public CacheRevocationTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    [Fact]
    public async Task Pausing_announces_the_write_so_the_receiving_path_stops_seeing_it_as_running()
    {
        int subscriptionId;
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, Unique("Pause revoke doc"), DocumentFormat.Json);
            db.Set<Document>().Add(document);
            await db.SaveChangesAsync();

            var subscription = new Subscription(Unique("Pause revoke"), document.Id) { Inactive = false };
            db.Set<Subscription>().Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }

        var recorder = new RecordingCache();
        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var pause = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Pause>(
                scope.ServiceProvider, recorder);
            await pause.Handle(subscriptionId, new SubscriptionPause());
        }

        // Without this, XchangeService keeps reading PausedOn off a copy taken before the pause and
        // goes on creating ordinary exchanges for an integration the operator has stopped. Resuming
        // has the mirror failure: the handler finds its own cached copy still paused and returns
        // without releasing anything it held.
        Assert.Equal(1, recorder.Broadcasts);
    }

    [Fact]
    public async Task Revoking_clears_global_values_too()
    {
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();
        var id = Unique("global-revoke");

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Set<GlobalAdapterValuesSet>().Add(new GlobalAdapterValuesSet
            {
                Id = id,
                Name = "Before",
                Values = new(),
            });
            await db.SaveChangesAsync();
        }

        cache.Revoke();
        Assert.Equal("Before", (await cache.GlobalAdapterValuesSetById(id))?.Name);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var entity = await db.Set<GlobalAdapterValuesSet>().FindAsync(id);
            entity!.Name = "After";
            await db.SaveChangesAsync();
        }

        // Load() caches global values but Revoke() used to skip them, so this read returned
        // "Before" no matter what had been revoked, until the ten minutes were up.
        cache.Revoke();
        Assert.Equal("After", (await cache.GlobalAdapterValuesSetById(id))?.Name);
    }

    /// <summary>
    /// Counts announcements and refuses everything else, so a handler that starts reading through
    /// the cache fails here rather than quietly passing against a stub that answered.
    /// </summary>
    private sealed class RecordingCache : IInfolinkCache
    {
        public int Broadcasts { get; private set; }

        public Task BroadcastRevoke()
        {
            Broadcasts++;
            return Task.CompletedTask;
        }

        public void Revoke() => throw new NotSupportedException();
        public Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId) => throw new NotSupportedException();
        public Task<BusGatewayRoute[]> ListBusGatewayRoutesByDocumentAsync(int documentId) => throw new NotSupportedException();
        public Task<Notifier[]> ListNotifiersAsync() => throw new NotSupportedException();
        public Task<Subscription> SubscriptionByIdAsync(int subscriptionId) => throw new NotSupportedException();
        public Task<Document> DocumentByIdAsync(int documentId) => throw new NotSupportedException();
        public Task<Document> DocumentByNameAsync(string documentName) => throw new NotSupportedException();
        public Task<WorkGroup[]> ListWorkGroupsAsync() => throw new NotSupportedException();
        public Task<WorkGroup> WorkGroupByIdAsync(int workGroupId) => throw new NotSupportedException();
        public Task<WorkGroup> WorkGroupBySubscriptionIdAsync(int subscriptionId) => throw new NotSupportedException();
        public Task<GlobalAdapterValuesSet> GlobalAdapterValuesSetById(string id) => throw new NotSupportedException();
        public Task<GlobalAdapterValuesSet[]> ListGlobalAdapterValuesSetsAsync() => throw new NotSupportedException();
    }
}
