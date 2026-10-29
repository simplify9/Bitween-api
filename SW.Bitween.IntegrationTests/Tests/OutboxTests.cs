using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Messages are written to the outbox with the change that raised them, published straight after
/// the commit, and published later by the dispatcher when that fails. Delivery is therefore at
/// least once, and processing has to shrug off a repeat.
/// </summary>
[Collection("Bitween")]
public class OutboxTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_new_exchange_is_announced_through_the_outbox_and_its_row_deleted_once_published()
    {
        var (xchangeId, _) = await CreateExchangeAsync();

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Body.Contains(xchangeId))
            .SingleOrDefaultAsync();

        // Published straight after the commit, as messages always were, and handed to the
        // acknowledger rather than marked: still there until its batch runs, never marked published.
        Assert.NotNull(row);
        Assert.Null(row.PublishedOn);

        await fixture.App.Services.GetRequiredService<OutboxAcknowledger>().FlushAsync(CancellationToken.None);
        Assert.False(await db.Set<OutboxMessage>().AsNoTracking().AnyAsync(m => m.Body.Contains(xchangeId)));
    }

    [Fact]
    public async Task A_message_that_cannot_be_published_is_kept_for_the_dispatcher_with_its_error()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Saved while the broker refuses: the publish straight after the commit fails.
        await using var unreachable = ActivatorUtilities.CreateInstance<PgSql.BitweenDbContext>(scope.ServiceProvider,
            new FailingPublish());
        var message = new OutboxMessage("OutboxTestMessage", JsonConvert.SerializeObject(new { Id = Guid.NewGuid() }));
        unreachable.Add(message);
        await unreachable.SaveChangesAsync();
        await fixture.App.Services.GetRequiredService<OutboxAcknowledger>().FlushAsync(CancellationToken.None);

        var row = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Null(row.PublishedOn);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("broker unreachable", row.LastError);
    }

    private sealed class FailingPublish : IPublish
    {
        public Task Publish<TMessage>(TMessage message) => throw new InvalidOperationException("broker unreachable");
        public Task Publish(string messageTypeName, string message) => throw new InvalidOperationException("broker unreachable");
        public Task Publish(string messageTypeName, byte[] message) => throw new InvalidOperationException("broker unreachable");
    }

    [Fact]
    public async Task A_message_left_unpublished_is_published_by_the_dispatcher()
    {
        // Rows other tests published are still queued for deletion; out of the way first, so the
        // dispatcher's batch reaches this one.
        await fixture.App.Services.GetRequiredService<OutboxAcknowledger>().FlushAsync(CancellationToken.None);

        long id;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var message = new OutboxMessage("OutboxTestMessage", JsonConvert.SerializeObject(new { Id = Guid.NewGuid() }));
            db.Add(message);
            await db.SaveChangesAsync();
            id = message.Id;

            // Back to unpublished, and old enough for the dispatcher: the state a broker outage at
            // commit time leaves behind.
            var longAgo = DateTime.UtcNow.AddMinutes(-5);
            await db.Set<OutboxMessage>().Where(m => m.Id == id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.CreatedOn, longAgo)
                    .SetProperty(m => m.PublishedOn, (DateTime?)null));
        }

        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(fixture.App.Services);
        Assert.True(await dispatcher.DispatchOnceAsync(CancellationToken.None) >= 1);
        await fixture.App.Services.GetRequiredService<OutboxAcknowledger>().FlushAsync(CancellationToken.None);

        // Published, and so deleted: what is left in the outbox is only what still has to go out.
        await using var check = fixture.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<OutboxMessage>().AsNoTracking().AnyAsync(m => m.Id == id));
    }

    /// <summary>
    /// A repeat delivery - after a crash between commit and ack, or the dispatcher publishing a
    /// message whose first publish had gone through - used to run the handler again and deliver
    /// to the partner twice.
    /// </summary>
    [Fact]
    public async Task A_repeat_delivery_of_a_processed_exchange_does_not_deliver_again()
    {
        var (xchangeId, token) = await CreateExchangeAsync();

        for (var delivery = 0; delivery < 2; delivery++)
        {
            await using var scope = fixture.CreateScope();
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("OutboxRepeat", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(token));

        await using var check = fixture.CreateScope();
        var result = await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.True(result.Success, result.Exception);
    }

    private async Task<(string XchangeId, string Token)> CreateExchangeAsync()
    {
        var token = $"outbox-{Guid.NewGuid():N}";
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, $"Outbox {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Outbox {Guid.NewGuid():N}", document.Id)
            { Inactive = false, HandlerId = nameof(NativeTestResponder) };
        subscription.SetDictionaries(
            new Dictionary<string, string> { ["Body"] = token },
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        return (xchange.Id, token);
    }
}
