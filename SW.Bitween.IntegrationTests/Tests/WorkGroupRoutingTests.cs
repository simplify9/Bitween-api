using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Xchanges;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// An exchange is sent to its subscription's work group queue. Each path is run on a fresh
/// DbContext, the way it runs in production: in the context that seeded the data the work group is
/// already tracked, EF fills the subscription's navigation property on its own, and a loader that
/// forgets to include it looks correct.
/// </summary>
[Collection("Bitween")]
public class WorkGroupRoutingTests(BitweenFixture fixture)
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the queue each new exchange for <paramref name="subscriptionId"/> is published to.
    /// Read while saving, because the save publishes the events and then clears them.
    /// </summary>
    private static List<string> RecordQueues(BitweenDbContext db, int subscriptionId)
    {
        var queues = new List<string>();
        db.SavingChanges += (_, _) => queues.AddRange(db.ChangeTracker.Entries<Xchange>()
            .Where(e => e.State == EntityState.Added && e.Entity.SubscriptionId == subscriptionId)
            .SelectMany(e => e.Entity.Events.OfType<IHasWorkGroup>())
            .Select(ev => ev.GetBusMessageName()));
        return queues;
    }

    private static async Task<WorkGroup> AddWorkGroup(BitweenDbContext db)
    {
        var workGroup = new WorkGroup
        {
            Name = "Routing " + Guid.NewGuid().ToString("N")[..8],
            BusMessageName = "routing" + Guid.NewGuid().ToString("N")[..8]
        };
        db.Add(workGroup);
        await db.SaveChangesAsync();
        return workGroup;
    }

    private static async Task<Document> AddDocument(BitweenDbContext db, string name)
    {
        var doc = new Document(null, name, DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    // ─── Paths that load the subscription themselves ──────────────────────────

    [Fact]
    public async Task Delayed_retry_runs_in_the_subscriptions_work_group()
    {
        int subscriptionId;
        string expected;
        await using (var seed = fixture.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = seed.ServiceProvider.GetRequiredService<XchangeService>();

            var workGroup = await AddWorkGroup(db);
            var doc = await AddDocument(db, "WG Routing Retry Doc");
            var sub = new Subscription("WG Routing Retry Sub", doc.Id) { Inactive = false, WorkGroupId = workGroup.Id };
            db.Set<Subscription>().Add(sub);
            await db.SaveChangesAsync();

            var original = await xs.CreateXchange(sub, new XchangeFile("{}"));
            await db.SaveChangesAsync();
            db.Set<XchangeResult>().Add(new XchangeResult(original.Id, null, null, exception: "boom"));
            db.Set<DelayedRetry>().Add(new DelayedRetry { Id = original.Id, On = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();

            subscriptionId = sub.Id;
            expected = workGroup.GetBusMessageName();
        }
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();

        await using var scope = fixture.CreateScope();
        var runDb = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var queues = RecordQueues(runDb, subscriptionId);

        await new RetryJob(runDb, scope.ServiceProvider.GetRequiredService<XchangeService>(),
            NullLogger<RetryJob>.Instance).Execute();

        Assert.Equal(expected, Assert.Single(queues));
    }

    [Fact]
    public async Task Manual_create_for_a_subscription_runs_in_its_work_group()
    {
        int subscriptionId;
        string expected;
        await using (var seed = fixture.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var workGroup = await AddWorkGroup(db);
            var doc = await AddDocument(db, "WG Routing Create Doc");
            var sub = new Subscription("WG Routing Create Sub", doc.Id) { Inactive = false, WorkGroupId = workGroup.Id };
            db.Set<Subscription>().Add(sub);
            await db.SaveChangesAsync();

            subscriptionId = sub.Id;
            expected = workGroup.GetBusMessageName();
        }
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();

        await using var scope = fixture.CreateScope();
        var runDb = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var queues = RecordQueues(runDb, subscriptionId);

        await new Create(scope.ServiceProvider.GetRequiredService<XchangeService>(), runDb).Handle(new CreateXchange
        {
            Option = CreateXchangeOption.SubscriberId,
            SubscriberId = subscriptionId,
            Data = "{}"
        });

        Assert.Equal(expected, Assert.Single(queues));
    }

    [Fact]
    public async Task Aggregation_runs_in_the_aggregation_subscriptions_work_group()
    {
        int aggSubId;
        string expected;
        await using (var seed = fixture.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var workGroup = await AddWorkGroup(db);
            var sourceDoc = await AddDocument(db, "WG Routing Agg Source Doc");
            var sourceSub = new Subscription("WG Routing Agg Source", sourceDoc.Id) { Inactive = false };
            db.Set<Subscription>().Add(sourceSub);
            await db.SaveChangesAsync();

            var source = new Xchange(sourceSub, new XchangeFile("{\"n\":1}"));
            db.Set<Xchange>().Add(source);
            await db.SaveChangesAsync();
            db.Set<XchangeResult>().Add(new XchangeResult(source.Id, null, null));
            await db.SaveChangesAsync();

            var aggSub = new Subscription("WG Routing Agg", sourceSub.Id, Partner.SystemId)
            {
                Inactive = false,
                AggregationTarget = XchangeFileType.Input,
                WorkGroupId = workGroup.Id
            };
            db.Set<Subscription>().Add(aggSub);
            await db.SaveChangesAsync();

            aggSubId = aggSub.Id;
            expected = workGroup.GetBusMessageName();
        }
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();

        await using var scope = fixture.CreateScope();
        var runDb = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var queues = RecordQueues(runDb, aggSubId);

        await scope.ServiceProvider.GetRequiredService<AggregationJob>().Execute(new AggregationJobParams(aggSubId, null));

        Assert.Equal(expected, Assert.Single(queues));
    }
}
