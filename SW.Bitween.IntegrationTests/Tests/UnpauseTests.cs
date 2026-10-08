using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Unpausing releases what was held while the subscription was paused. It is released in batches,
/// so a long pause can't load the whole backlog — payloads and all — into memory at once.
/// </summary>
[Collection("Bitween")]
public class UnpauseTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_backlog_bigger_than_a_batch_is_released_in_full()
    {
        const int held = 120;
        int subscriptionId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, $"Unpause {Guid.NewGuid():N}", DocumentFormat.Json);
            db.Add(document);
            await db.SaveChangesAsync();

            var subscription = new Subscription($"Unpause {Guid.NewGuid():N}", document.Id) { Inactive = false };
            db.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;

            for (var i = 0; i < held; i++)
                db.Add(new OnHoldXchange(subscription, $"{{\"n\":{i}}}"));
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process(new SubscriptionUnpausedEvent { Id = subscriptionId });
        }

        await using var check = fixture.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await checkDb.Set<OnHoldXchange>().AnyAsync(x => x.SubscriptionId == subscriptionId));
        Assert.Equal(held, await checkDb.Set<Xchange>().CountAsync(x => x.SubscriptionId == subscriptionId));
    }
}
