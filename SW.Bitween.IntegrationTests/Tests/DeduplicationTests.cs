using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.DataSources;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Deduplication of inbound messages.
///
/// At-least-once delivery is what persist-then-acknowledge buys: a crash between committing the
/// Xchange and acknowledging the broker redelivers by design. Duplicates are therefore normal, and
/// these tests are about recognising them without either losing a message or looping on one.
/// </summary>
[Collection("Bitween")]
public class DeduplicationTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_redelivered_message_does_not_produce_a_second_Xchange()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var first = await sink.OnEventAsync(Event(setup, "dup-key-1"), CancellationToken.None);
        var second = await sink.OnEventAsync(Event(setup, "dup-key-1"), CancellationToken.None);

        Assert.True(first.Accepted);

        // ACCEPTED, not rejected. Rejecting would nack and redeliver a message that is by
        // definition already handled, and the queue would never drain.
        Assert.True(second.Accepted, "a duplicate must be accepted so the broker stops resending it");

        Assert.Equal(1, await XchangeCountAsync(setup.DocumentId));
    }

    /// <summary>
    /// The point of making the DATABASE the arbiter. "Look it up, then insert if absent" is
    /// check-then-act: two concurrent deliveries of one key both miss and both persist. Only a
    /// unique constraint decides this correctly, and only concurrency proves it is the constraint
    /// doing the work rather than a lookup that happened to be lucky.
    /// </summary>
    [Fact]
    public async Task Concurrent_deliveries_of_one_key_produce_exactly_one_Xchange()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => sink.OnEventAsync(Event(setup, "race-key"), CancellationToken.None)));

        Assert.All(outcomes, o => Assert.True(o.Accepted, o.Error));
        Assert.Equal(1, await XchangeCountAsync(setup.DocumentId));
    }

    [Fact]
    public async Task Different_keys_are_not_confused_for_each_other()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        await sink.OnEventAsync(Event(setup, "key-a"), CancellationToken.None);
        await sink.OnEventAsync(Event(setup, "key-b"), CancellationToken.None);

        Assert.Equal(2, await XchangeCountAsync(setup.DocumentId));
    }

    /// <summary>
    /// SP-API notification ids are globally unique, so two data sources subscribed to the same
    /// notification would silently deduplicate against each other if the key were not namespaced.
    /// That might occasionally be wanted; it must never happen by accident.
    /// </summary>
    [Fact]
    public async Task The_same_key_on_two_data_sources_is_two_different_messages()
    {
        var first = await ArrangeAsync();
        var second = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        await sink.OnEventAsync(Event(first, "spapi:notif-shared"), CancellationToken.None);
        await sink.OnEventAsync(Event(second, "spapi:notif-shared"), CancellationToken.None);

        Assert.Equal(1, await XchangeCountAsync(first.DocumentId));
        Assert.Equal(1, await XchangeCountAsync(second.DocumentId));
    }

    [Fact]
    public async Task An_event_with_no_key_is_never_deduplicated()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        // Nothing identifies these as the same message, so both must be persisted. Silently
        // collapsing unidentified messages would lose data.
        await sink.OnEventAsync(Event(setup, dedupeKey: null), CancellationToken.None);
        await sink.OnEventAsync(Event(setup, dedupeKey: null), CancellationToken.None);

        Assert.Equal(2, await XchangeCountAsync(setup.DocumentId));
    }

    [Fact]
    public async Task A_window_of_zero_turns_deduplication_off()
    {
        var setup = await ArrangeAsync(deduplicationWindowDays: 0);
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        await sink.OnEventAsync(Event(setup, "off-key"), CancellationToken.None);
        await sink.OnEventAsync(Event(setup, "off-key"), CancellationToken.None);

        Assert.Equal(2, await XchangeCountAsync(setup.DocumentId));
    }

    /// <summary>
    /// Atomicity. A failed ingest must NOT leave the key behind - remembering a message that was
    /// never persisted suppresses its redelivery for ever, which is silent data loss and the worse
    /// of the two failure modes.
    /// </summary>
    [Fact]
    public async Task A_failed_ingest_does_not_remember_the_key()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        // No gateway claims this endpoint, so nothing is persisted for it.
        var outcome = await sink.OnEventAsync(new InboundEvent
        {
            AdapterId = BusAdapters.RabbitMq,
            InstanceKey = setup.DataSourceId.ToString(),
            Endpoint = "an-endpoint-no-gateway-claims",
            DedupeKey = "unpersisted-key",
            Payload = Encoding.UTF8.GetBytes("{}")
        }, CancellationToken.None);

        Assert.True(outcome.Accepted);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        Assert.False(
            await db.Set<InboundMessage>().AnyAsync(m => m.Id.EndsWith("unpersisted-key")),
            "a key was remembered for a message that was never persisted, so its redelivery would " +
            "be silently discarded");
    }

    // ---------------------------------------------------------------- retention

    [Fact]
    public async Task Pruning_forgets_keys_past_the_window_and_keeps_the_rest()
    {
        var setup = await ArrangeAsync(deduplicationWindowDays: 7);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            db.Add(Aged(setup.DataSourceId, "old-key", DateTime.UtcNow.AddDays(-30)));
            db.Add(Aged(setup.DataSourceId, "recent-key", DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<InboundMessagePruneJob>(scope.ServiceProvider);
            await job.Execute();
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            Assert.False(await db.Set<InboundMessage>().AnyAsync(m => m.Id.EndsWith("old-key")));
            Assert.True(await db.Set<InboundMessage>().AnyAsync(m => m.Id.EndsWith("recent-key")),
                "forgetting a key too early lets a redelivery through as a fresh message");
        }
    }

    /// <summary>Deleting a data source must take its keys with it rather than orphaning them.</summary>
    [Fact]
    public async Task Deleting_a_data_source_forgets_its_keys()
    {
        var setup = await ArrangeAsync();
        var sink = fixture.App.Services.GetRequiredService<IAdapterEventSink>();

        await sink.OnEventAsync(Event(setup, "cascade-key"), CancellationToken.None);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        Assert.True(await db.Set<InboundMessage>().AnyAsync(m => m.DataSourceId == setup.DataSourceId));

        db.RemoveRange(db.Set<BusGateway>().Where(g => g.DataSourceId == setup.DataSourceId));
        await db.SaveChangesAsync();

        db.Remove(await db.Set<DataSource>().FirstAsync(d => d.Id == setup.DataSourceId));
        await db.SaveChangesAsync();

        Assert.False(await db.Set<InboundMessage>().AnyAsync(m => m.DataSourceId == setup.DataSourceId));
    }

    // ---------------------------------------------------------------- helpers

    private record Setup(int DocumentId, int DataSourceId, string Endpoint);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static InboundMessage Aged(int dataSourceId, string key, DateTime seenOn)
    {
        var message = new InboundMessage($"{dataSourceId}:{key}", dataSourceId, "x");

        // SeenOn is set by the constructor and is private; ageing it is what the test needs.
        typeof(InboundMessage).GetProperty(nameof(InboundMessage.SeenOn))!
            .SetValue(message, seenOn);

        return message;
    }

    private static InboundEvent Event(Setup setup, string? dedupeKey) => new()
    {
        AdapterId = BusAdapters.RabbitMq,
        InstanceKey = setup.DataSourceId.ToString(),
        Endpoint = setup.Endpoint,
        DedupeKey = dedupeKey,
        Payload = Encoding.UTF8.GetBytes("{\"orderId\":1}")
    };

    private async Task<int> XchangeCountAsync(int documentId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Xchange>().CountAsync(x => x.DocumentId == documentId && x.SubscriptionId == null);
    }

    private async Task<Setup> ArrangeAsync(int deduplicationWindowDays = 30)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var endpoint = Unique("dedupe-q");

        var dataSource = new DataSource
        {
            Name = Unique("dedupe-ds"),
            AdapterId = BusAdapters.RabbitMq,
            Kind = DataSourceKind.Broker,
            DeduplicationWindowDays = deduplicationWindowDays,
            Properties = new Dictionary<string, string> { ["Endpoints"] = endpoint }
        };
        db.Add(dataSource);
        await db.SaveChangesAsync();

        var document = new Document(null, Unique("dedupe-doc"), DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        db.Add(new BusGateway
        {
            Name = Unique("dedupe-gw"),
            DocumentId = document.Id,
            DataSourceId = dataSource.Id,
            Endpoint = endpoint
        });
        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        return new Setup(document.Id, dataSource.Id, endpoint);
    }
}
