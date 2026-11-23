using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Every exchange carries its own status and whether it was retried, written in the same save as
/// the result or the retry that decides them, by every path that does.
/// </summary>
[Collection("Bitween")]
public class ExchangeStateTests(BitweenFixture fixture)
{
    private const string ConfigurableAdapter = "sw.bitween.sampleconfigurableadapter";

    [Fact]
    public async Task An_exchange_is_processing_until_it_runs_then_carries_its_outcome()
    {
        string okId, failId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

            var doc = new Document(null, "State Outcome Doc", DocumentFormat.Json);
            db.Set<Document>().Add(doc);
            await db.SaveChangesAsync();

            var ok = Subscription(db, "State Ok Sub", doc.Id, new Dictionary<string, string>());
            var fail = Subscription(db, "State Fail Sub", doc.Id,
                new Dictionary<string, string> { ["SimulateError"] = "true", ["ErrorMessage"] = "state boom" });
            await db.SaveChangesAsync();

            // A document created after the cache warmed is invisible to the run otherwise.
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

            okId = (await xs.CreateXchange(ok, new XchangeFile("{}"))).Id;
            failId = (await xs.CreateXchange(fail, new XchangeFile("{}"))).Id;
            await db.SaveChangesAsync();
        }

        Assert.Equal(XchangeStatus.Processing, (await Load(okId)).Status);
        Assert.Equal(XchangeStatus.Processing, (await Load(failId)).Status);

        await Run(okId);
        await Run(failId);

        var okXchange = await Load(okId);
        var failXchange = await Load(failId);
        Assert.True((await LoadResult(okId)).Success);
        Assert.False((await LoadResult(failId)).Success);
        Assert.Null(okXchange.Status);
        Assert.Equal(XchangeStatus.Failed, failXchange.Status);
        Assert.Null(okXchange.Retried);
        Assert.Null(failXchange.Retried);
    }

    [Fact]
    public async Task Every_kind_of_retry_marks_the_exchange_it_retries()
    {
        string byHand, byHandReset, scheduled, bulk, untouched;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

            var doc = new Document(null, "State Retry Doc", DocumentFormat.Json);
            db.Set<Document>().Add(doc);
            await db.SaveChangesAsync();
            var sub = new Subscription("State Retry Sub", doc.Id) { Inactive = false };
            db.Set<Subscription>().Add(sub);
            await db.SaveChangesAsync();

            async Task<string> Failed()
            {
                var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
                await db.SaveChangesAsync();
                db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null, exception: "boom"));
                await db.SaveChangesAsync();
                return xchange.Id;
            }

            byHand = await Failed();
            byHandReset = await Failed();
            scheduled = await Failed();
            bulk = await Failed();
            untouched = await Failed();
        }

        // Each in its own scope, as each arrives in production: a request, or a job's run. That also
        // makes bulk retry read its original untracked, which is the case that needs attaching.
        await InScope(async (db, xs, ctx) =>
            await new Resources.Xchanges.Retry(db, ctx, xs).Handle(byHand, new XchangeRetry { Reset = false }));
        await InScope(async (db, xs, ctx) =>
            await new Resources.Xchanges.Retry(db, ctx, xs).Handle(byHandReset, new XchangeRetry { Reset = true }));
        await InScope(async (db, xs, _) =>
        {
            var delayed = new DelayedRetry { Id = scheduled, On = DateTime.UtcNow.AddMinutes(-1) };
            db.Set<DelayedRetry>().Add(delayed);
            await db.SaveChangesAsync();
            Assert.True(await xs.ExecuteDelayedRetry(delayed));
        });
        await InScope(async (db, xs, ctx) =>
        {
            var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
                .Handle(new XchangeBulkRetry { Ids = [bulk] });
            Assert.Equal(1, plan.WillRetry);
        });

        foreach (var id in new[] { byHand, byHandReset, scheduled, bulk })
        {
            Assert.True((await Load(id)).Retried, $"{id} was retried but isn't marked");
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == id));
        }

        Assert.Null((await Load(untouched)).Retried);
    }

    private static Subscription Subscription(BitweenDbContext db, string name, int documentId,
        Dictionary<string, string> handlerProperties)
    {
        var sub = new Subscription(name, documentId) { Inactive = false };
        sub.HandlerId = ConfigurableAdapter;
        sub.SetDictionaries(handlerProperties, null, null, null, null);
        db.Set<Subscription>().Add(sub);
        return sub;
    }

    private async Task InScope(Func<BitweenDbContext, XchangeService, RequestContext, Task> act)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        await act(db, xs, scope.Superuser());
        await db.SaveChangesAsync();
    }

    /// <summary>Runs the exchange through the same entry point the bus calls.</summary>
    private async Task Run(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
    }

    private async Task<Xchange> Load(string id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<XchangeResult> LoadResult(string id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == id);
    }
}
