using System;
using System.Collections.Generic;
using System.Linq;
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
}
