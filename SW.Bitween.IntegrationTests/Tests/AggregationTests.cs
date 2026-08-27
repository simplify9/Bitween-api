using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class AggregationTests
{
    private readonly BitweenFixture _fixture;

    public AggregationTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Aggregation_job_creates_one_xchange_from_successful_source_xchanges()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        // Source subscription whose Xchanges will be aggregated
        var sourceDoc = new Document(null, "Agg Source Doc", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        var sourceSub = new Subscription("Agg Source", sourceDoc.Id);
        sourceSub.Inactive = false;
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        // Create 3 source Xchanges with successful results
        var xchangeIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var xchange = new Xchange(sourceSub, new XchangeFile($"{{\"i\":{i}}}"));
            db.Set<Xchange>().Add(xchange);
            await db.SaveChangesAsync();

            db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null));
            await db.SaveChangesAsync();

            xchangeIds.Add(xchange.Id);
        }

        // Aggregation subscription pointing at the source
        var aggSub = new Subscription("Agg Test", sourceSub.Id, Partner.SystemId);
        aggSub.Inactive = false;
        aggSub.AggregationTarget = XchangeFileType.Input;
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        // One aggregation Xchange should have been created
        var aggXchange = await db.Set<Xchange>().FirstOrDefaultAsync(x => x.SubscriptionId == aggSub.Id);
        Assert.NotNull(aggXchange);

        // All 3 source Xchanges should now be marked as aggregated
        var aggLinks = await db.Set<XchangeAggregation>()
            .Where(a => xchangeIds.Contains(a.Id))
            .ToListAsync();
        Assert.Equal(3, aggLinks.Count);
        Assert.All(aggLinks, a => Assert.Equal(aggXchange.Id, a.AggregationXchangeId));
    }

    [Fact]
    public async Task Aggregation_job_skips_already_aggregated_xchanges()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var sourceDoc = new Document(null, "Agg Source Doc 2", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        var sourceSub = new Subscription("Agg Source 2", sourceDoc.Id);
        sourceSub.Inactive = false;
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        // Create 2 source Xchanges — one will be pre-aggregated, one will not
        var x1 = new Xchange(sourceSub, new XchangeFile("{\"seq\":1}"));
        var x2 = new Xchange(sourceSub, new XchangeFile("{\"seq\":2}"));
        db.Set<Xchange>().Add(x1);
        db.Set<Xchange>().Add(x2);
        await db.SaveChangesAsync();

        db.Set<XchangeResult>().Add(new XchangeResult(x1.Id, null, null));
        db.Set<XchangeResult>().Add(new XchangeResult(x2.Id, null, null));
        await db.SaveChangesAsync();

        // Mark x1 as already aggregated
        var priorAggXchange = new Xchange(sourceSub, new XchangeFile("{\"prior\":true}"));
        db.Set<Xchange>().Add(priorAggXchange);
        await db.SaveChangesAsync();
        db.Set<XchangeAggregation>().Add(new XchangeAggregation(x1.Id, priorAggXchange.Id));
        await db.SaveChangesAsync();

        var aggSub = new Subscription("Agg Test 2", sourceSub.Id, Partner.SystemId);
        aggSub.Inactive = false;
        aggSub.AggregationTarget = XchangeFileType.Input;
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        // Only x2 was eligible → one aggregation Xchange created
        var aggXchanges = await db.Set<Xchange>()
            .Where(x => x.SubscriptionId == aggSub.Id)
            .ToListAsync();
        Assert.Single(aggXchanges);

        // x2 now has an aggregation link; x1 still points to the prior one
        var x2Link = await db.Set<XchangeAggregation>().FindAsync(x2.Id);
        Assert.NotNull(x2Link);
        Assert.Equal(aggXchanges[0].Id, x2Link.AggregationXchangeId);
    }

    [Fact]
    public async Task Aggregation_job_does_nothing_for_inactive_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();

        var sourceDoc = new Document(null, "Inactive Agg Source Doc", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        var sourceSub = new Subscription("Inactive Agg Source", sourceDoc.Id);
        sourceSub.Inactive = false;
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        var aggSub = new Subscription("Inactive Agg", sourceSub.Id, Partner.SystemId);
        // Inactive = true by default
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        var count = await db.Set<Xchange>().CountAsync(x => x.SubscriptionId == aggSub.Id);
        Assert.Equal(0, count);
    }

    // ─── Run history ────────────────────────────────────────────────────────
    //
    // Every run is written into the same history a receiver's runs go into, so the integration
    // page can show one table of runs with the exchange each produced instead of two tables
    // that cannot be joined. Before this, an aggregation recorded nothing at all.

    [Fact]
    public async Task A_run_that_rolls_something_up_records_the_exchange_it_made()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var sourceDoc = new Document(null, "Agg Attempt Doc", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        var sourceSub = new Subscription("Agg Attempt Source", sourceDoc.Id);
        sourceSub.Inactive = false;
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        var xchange = new Xchange(sourceSub, new XchangeFile("{\"n\":1}"));
        db.Set<Xchange>().Add(xchange);
        await db.SaveChangesAsync();
        db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null));
        await db.SaveChangesAsync();

        var aggSub = new Subscription("Agg Attempt", sourceSub.Id, Partner.SystemId);
        aggSub.Inactive = false;
        // A real aggregation always has one — SetSchedules throws without it, and the run would
        // then report a failure it did not have.
        aggSub.SetSchedules([new Schedule(Recurrence.Daily, TimeSpan.FromHours(2))]);
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        var attempt = await db.Set<ReceiveAttempt>().SingleAsync(a => a.SubscriptionId == aggSub.Id);
        Assert.Equal(ReceiveOutcome.Received, attempt.Outcome);
        Assert.Null(attempt.ErrorMessage);

        var rollUp = await db.Set<Xchange>().SingleAsync(x => x.SubscriptionId == aggSub.Id);
        Assert.Equal([rollUp.Id], attempt.ExchangeIds);
    }

    [Fact]
    public async Task A_run_with_nothing_outstanding_records_no_new_data_rather_than_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var sourceDoc = new Document(null, "Agg Empty Attempt Doc", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        // A source with no exchanges at all — the run has nothing to collect.
        var sourceSub = new Subscription("Agg Empty Attempt Source", sourceDoc.Id);
        sourceSub.Inactive = false;
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        var aggSub = new Subscription("Agg Empty Attempt", sourceSub.Id, Partner.SystemId);
        aggSub.Inactive = false;
        // A real aggregation always has one — SetSchedules throws without it, and the run would
        // then report a failure it did not have.
        aggSub.SetSchedules([new Schedule(Recurrence.Daily, TimeSpan.FromHours(2))]);
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        var attempt = await db.Set<ReceiveAttempt>().SingleAsync(a => a.SubscriptionId == aggSub.Id);
        Assert.Equal(ReceiveOutcome.NoNewData, attempt.Outcome);
        Assert.Empty(attempt.ExchangeIds);
        // The quiet outcome still creates no exchange — that part has not changed.
        Assert.Equal(0, await db.Set<Xchange>().CountAsync(x => x.SubscriptionId == aggSub.Id));
    }

    [Fact]
    public async Task An_inactive_aggregation_records_no_run_at_all()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<AggregationJob>();

        var sourceDoc = new Document(null, "Agg Skipped Attempt Doc", DocumentFormat.Json);
        db.Set<Document>().Add(sourceDoc);
        await db.SaveChangesAsync();
        var sourceSub = new Subscription("Agg Skipped Attempt Source", sourceDoc.Id);
        db.Set<Subscription>().Add(sourceSub);
        await db.SaveChangesAsync();

        // Inactive by default — the job returns before it does anything, and a run that never
        // happened must not appear in the history as a quiet success.
        var aggSub = new Subscription("Agg Skipped Attempt", sourceSub.Id, Partner.SystemId);
        db.Set<Subscription>().Add(aggSub);
        await db.SaveChangesAsync();

        await job.Execute(new AggregationJobParams(aggSub.Id, null));

        Assert.Empty(await db.Set<ReceiveAttempt>().Where(a => a.SubscriptionId == aggSub.Id).ToListAsync());
    }

    // ─── Configuration ──────────────────────────────────────────────────────
    //
    // Which file a roll-up links to is the one aggregation setting no UI has ever offered.
    // It reached the entity only through the generic property copy in the update handler, so
    // create could not set it at all and every aggregation started on Input whatever the
    // caller asked for. These pin the field to the shared applier both handlers run.

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>An integration to roll up, and a partner to attribute the roll-up to.</summary>
    private async Task<(int sourceId, int partnerId)> Groundwork()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(null, Unique("Agg config doc"), DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        var partner = new Partner(Unique("Agg config partner"));
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var source = new Subscription(Unique("Agg config source"), doc.Id);
        db.Set<Subscription>().Add(source);
        await db.SaveChangesAsync();

        return (source.Id, partner.Id);
    }

    private static ScheduleView[] Daily() =>
        [new ScheduleView { Recurrence = Recurrence.Daily, Hours = 2 }];

    private async Task<int> CreateAggregation(int sourceId, int partnerId, XchangeFileType? target)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);

        var model = new SubscriptionCreate
        {
            Name = Unique("Agg config"),
            Type = SubscriptionType.Aggregation,
            AggregationForId = sourceId,
            PartnerId = partnerId,
            Schedules = Daily(),
        };
        // Left unset on purpose by the caller testing the default, so that the test proves the
        // default rather than the applier's willingness to copy whatever it was handed.
        if (target.HasValue) model.AggregationTarget = target.Value;

        return (int)await handler.Handle(model);
    }

    [Fact]
    public async Task An_aggregation_can_be_created_collecting_the_mapped_file()
    {
        var (sourceId, partnerId) = await Groundwork();
        var id = await CreateAggregation(sourceId, partnerId, XchangeFileType.Output);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var entity = await db.Set<Subscription>().SingleAsync(s => s.Id == id);

        Assert.Equal(XchangeFileType.Output, entity.AggregationTarget);
    }

    [Fact]
    public async Task An_aggregation_collects_what_came_in_unless_told_otherwise()
    {
        var (sourceId, partnerId) = await Groundwork();
        var id = await CreateAggregation(sourceId, partnerId, null);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var entity = await db.Set<Subscription>().SingleAsync(s => s.Id == id);

        Assert.Equal(XchangeFileType.Input, entity.AggregationTarget);
    }

    [Fact]
    public async Task Changing_which_file_is_collected_is_kept()
    {
        var (sourceId, partnerId) = await Groundwork();
        var id = await CreateAggregation(sourceId, partnerId, XchangeFileType.Input);

        await using (var scope = _fixture.CreateScope())
        {
            scope.Superuser();
            var update = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider);
            await update.Handle(id, new SubscriptionUpdate
            {
                Name = Unique("Agg config renamed"),
                AggregationForId = sourceId,
                PartnerId = partnerId,
                Schedules = Daily(),
                AggregationTarget = XchangeFileType.Response,
            });
        }

        await using var check = _fixture.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var entity = await db.Set<Subscription>().SingleAsync(s => s.Id == id);

        Assert.Equal(XchangeFileType.Response, entity.AggregationTarget);
    }

    [Fact]
    public async Task The_list_reports_when_an_aggregation_next_runs()
    {
        var (sourceId, partnerId) = await Groundwork();
        var id = await CreateAggregation(sourceId, partnerId, XchangeFileType.Output);

        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var search = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Search>(scope.ServiceProvider);

        var response = (SearchyResponse<SubscriptionSearch>)await search.Handle(
            new SearchyRequest { PageSize = 500, PageIndex = 0 });
        var row = response.Result.Single(r => r.Id == id);

        // ReceiveOn is only ever set for Receiving, and the list page reads one next-run field
        // for both scheduled types — so without AggregateOn in the projection every aggregation
        // row showed no next run at all, for a job that plainly has one.
        Assert.NotNull(row.AggregateOn);
        Assert.Null(row.ReceiveOn);
        Assert.Equal(XchangeFileType.Output, row.AggregationTarget);
    }
}
