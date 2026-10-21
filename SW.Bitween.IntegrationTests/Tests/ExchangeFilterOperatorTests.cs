using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Xchanges;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The exchange filters asked the other way round - "is not", "is none of", "does not contain",
/// "is missing" - and "is any of". Each is checked where it is used: the list, "select all
/// matching" (what bulk retry and export act on) and the per-status counts, which must agree.
/// </summary>
/// <remarks>
/// A turned-round filter keeps the exchanges that have no value at all: "Subscription is not A"
/// keeps the ones with no subscription, "Error does not contain timeout" keeps the ones with no
/// error. Every test scopes itself to an information type of its own, since the collection shares
/// one database.
/// </remarks>
[Collection("Bitween")]
public class ExchangeFilterOperatorTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Subscription_is_not_and_is_none_of_keep_everything_else_including_no_subscription()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Sub Doc");
        var a = await NewSubscription(db, "Operators A", doc.Id);
        var b = await NewSubscription(db, "Operators B", doc.Id);
        var c = await NewSubscription(db, "Operators C", doc.Id);

        var ofA = await xs.CreateXchange(a, new XchangeFile("{}"));
        var ofB = await xs.CreateXchange(b, new XchangeFile("{}"));
        var ofC = await xs.CreateXchange(c, new XchangeFile("{}"));
        var ofNone = await xs.CreateXchange(doc, null, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        var inDoc = $"DocumentId:1:{doc.Id}";
        Assert.Equal(Ids(ofB, ofC, ofNone), await Search(scope, inDoc, $"SubscriptionId:2:{a.Id}"));

        string[] noneOfAB = [inDoc, $"SubscriptionId:2:{a.Id}", $"SubscriptionId:2:{b.Id}"];
        Assert.Equal(Ids(ofC, ofNone), await Search(scope, noneOfAB));
        Assert.Equal(Ids(ofC, ofNone), await Select(scope, noneOfAB));
        // Neither has a result yet, so both count as still processing.
        Assert.Equal(2, (await Summarise(scope, noneOfAB)).Counts.Processing);

        Assert.Equal(Ids(ofA, ofB), await Search(scope, inDoc, $"SubscriptionId:9:numeric|{a.Id}|{b.Id}"));
        Assert.Equal(Ids(ofA, ofB), await Select(scope, inDoc, $"SubscriptionId:9:numeric|{a.Id}|{b.Id}"));
    }

    [Fact]
    public async Task Partner_and_information_type_take_the_same_operators()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Partner Doc");
        var other = await NewDocument(db, "Operators Other Doc");
        var p1 = new Partner($"Operators P1 {Guid.NewGuid():N}");
        var p2 = new Partner($"Operators P2 {Guid.NewGuid():N}");
        db.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var withP1 = await xs.CreateXchange(await NewSubscription(db, "Operators P1 sub", doc.Id, p1.Id), new XchangeFile("{}"));
        var withP2 = await xs.CreateXchange(await NewSubscription(db, "Operators P2 sub", doc.Id, p2.Id), new XchangeFile("{}"));
        var noPartner = await xs.CreateXchange(await NewSubscription(db, "Operators no partner", doc.Id), new XchangeFile("{}"));
        var otherSub = await NewSubscription(db, "Operators other doc sub", other.Id);
        var ofOther = await xs.CreateXchange(otherSub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        var inDoc = $"DocumentId:1:{doc.Id}";
        Assert.Equal(Ids(withP2, noPartner), await Search(scope, inDoc, $"PartnerId:2:{p1.Id}"));
        Assert.Equal(Ids(withP1, withP2), await Search(scope, inDoc, $"PartnerId:9:numeric|{p1.Id}|{p2.Id}"));

        // Information type turned round, kept to this test's own rows by their subscriptions.
        var mine = $"SubscriptionId:9:numeric|{withP1.SubscriptionId}|{otherSub.Id}";
        Assert.Equal(Ids(ofOther), await Search(scope, mine, $"DocumentId:2:{doc.Id}"));
        Assert.Equal(Ids(withP1, ofOther), await Search(scope, mine, $"DocumentId:9:numeric|{doc.Id}|{other.Id}"));
    }

    [Fact]
    public async Task Error_does_not_contain_keeps_the_exchanges_with_no_error()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Error Doc");
        var sub = await NewSubscription(db, "Operators Error", doc.Id);
        var running = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var succeeded = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var timedOut = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var mapping = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.AddRange(
            new XchangeResult(succeeded.Id, null, null),
            new XchangeResult(timedOut.Id, null, null, exception: "Timeout Talking To ACME after 30s"),
            new XchangeResult(mapping.Id, null, null, exception: "Mapping failed: no such field"));
        await db.SaveChangesAsync();

        string[] filters = [$"DocumentId:1:{doc.Id}", "ErrorContains:2:TIMEOUT"];
        Assert.Equal(Ids(running, succeeded, mapping), await Search(scope, filters));
        Assert.Equal(Ids(running, succeeded, mapping), await Select(scope, filters));
        var counts = (await Summarise(scope, filters)).Counts;
        Assert.Equal((1, 1, 1), (counts.Processing, counts.Success, counts.Failed));
    }

    [Fact]
    public async Task Each_yes_or_no_filter_turns_round()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Flags Doc");
        var sub = await NewSubscription(db, "Operators Flags", doc.Id);
        var failed = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var aggregated = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var scheduled = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Add(new XchangeResult(failed.Id, null, null, exception: "boom"));
        db.Add(new XchangeAggregation(aggregated.Id, failed.Id));
        // A day out, so the retry job cannot pick it up and delete the row mid-test.
        db.Add(new DelayedRetry { Id = scheduled.Id, On = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        await new Retry(db, scope.Superuser(), xs).Handle(failed.Id, new XchangeRetry { Reset = false });
        await db.SaveChangesAsync();
        var retry = await db.Set<Xchange>().SingleAsync(x => x.RetryFor == failed.Id);

        var inDoc = $"DocumentId:1:{doc.Id}";
        Assert.Equal(Ids(failed, aggregated, scheduled), await Search(scope, inDoc, "IsRetry:2:true"));
        Assert.Equal(Ids(failed, aggregated, retry.Id), await Search(scope, inDoc, "HasScheduledRetry:2:true"));
        Assert.Equal(Ids(failed, scheduled, retry.Id), await Select(scope, inDoc, "Aggregated:2:true"));
    }

    [Fact]
    public async Task A_property_filter_turns_round_whole_and_keeps_exchanges_that_promoted_nothing()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Property Doc");

        async Task<string> Exchange(params (string Key, string Value)[] properties)
        {
            var xchange = await xs.CreateXchange(doc, null, new XchangeFile("{}"));
            // Creating it reads its properties too, once the information type is in the cache -
            // which depends on what ran before. Replaced, so "promoted nothing" means no row at all.
            foreach (var read in db.ChangeTracker.Entries<XchangePromotedProperties>().Where(e => e.Entity.Id == xchange.Id).ToList())
                read.State = EntityState.Detached;
            if (properties.Length > 0)
            {
                var filtered = new FilterResult();
                foreach (var (key, value) in properties)
                    filtered.Properties[key] = value;
                db.Add(new XchangePromotedProperties(xchange.Id, filtered));
            }
            await db.SaveChangesAsync();
            return xchange.Id;
        }

        var a = await Exchange(("orderNo", "SO-1001-X"), ("customer", "Acme"));
        // "1001" sits in another key's value here.
        var b = await Exchange(("orderNo", "PO-7"), ("customer", "Zed-1001"));
        var none = await Exchange();

        var inDoc = $"DocumentId:1:{doc.Id}";
        // The key's own value only, turned round: b's 1001 is under customer, so b is kept.
        Assert.Equal(Ids(b, none), await Search(scope, inDoc, "PromotedPropertyKey:2:orderNo", "PromotedPropertiesRaw:2:1001"));
        Assert.Equal(Ids(b, none), await Select(scope, inDoc, "PromotedPropertyKey:2:orderNo", "PromotedPropertiesRaw:2:1001"));
        // The same question the right way round still finds a alone.
        Assert.Equal(Ids(a), await Search(scope, inDoc, "PromotedPropertyKey:1:orderNo", "PromotedPropertiesRaw:4:1001"));
        // A key on its own: "is missing".
        Assert.Equal(Ids(none), await Search(scope, inDoc, "PromotedPropertyKey:2:customer"));
        // No key: in no property at all.
        Assert.Equal(Ids(none), await Search(scope, inDoc, "PromotedPropertiesRaw:2:1001"));
        Assert.Equal(Ids(b, none), await Search(scope, inDoc, "PromotedPropertiesRaw:2:acme"));

        // One part turned round and the other not has no reading worth guessing at.
        await Assert.ThrowsAsync<SWValidationException>(() =>
            Search(scope, inDoc, "PromotedPropertyKey:1:orderNo", "PromotedPropertiesRaw:2:1001"));
    }

    [Fact]
    public async Task Several_properties_at_once_each_look_inside_their_own_key()
    {
        await using var scope = fixture.CreateScope();
        var (db, xs) = Services(scope);
        var doc = await NewDocument(db, "Operators Pairs Doc");

        async Task<string> Exchange(params (string Key, string Value)[] properties)
        {
            var xchange = await xs.CreateXchange(doc, null, new XchangeFile("{}"));
            foreach (var read in db.ChangeTracker.Entries<XchangePromotedProperties>().Where(e => e.Entity.Id == xchange.Id).ToList())
                read.State = EntityState.Detached;
            if (properties.Length > 0)
            {
                var filtered = new FilterResult();
                foreach (var (key, value) in properties)
                    filtered.Properties[key] = value;
                db.Add(new XchangePromotedProperties(xchange.Id, filtered));
            }
            await db.SaveChangesAsync();
            return xchange.Id;
        }

        var a = await Exchange(("orderNo", "SO-1001-X"), ("customer", "Acme"));
        var b = await Exchange(("orderNo", "PO-7"), ("customer", "Zed-1001"));
        var c = await Exchange(("orderNo", "SO-1002"), ("customer", "ACME Europe"));
        var none = await Exchange();

        var inDoc = $"DocumentId:1:{doc.Id}";
        Assert.Equal(Ids(a, c), await Search(scope, inDoc, "PromotedProperty:4:orderNo:SO", "PromotedProperty:4:customer:acme"));
        Assert.Equal(Ids(a), await Search(scope, inDoc, "PromotedProperty:4:orderNo:1001", "PromotedProperty:4:customer:acme"));
        Assert.Equal(Ids(a), await Select(scope, inDoc, "PromotedProperty:4:orderNo:1001", "PromotedProperty:4:customer:acme"));
        // Inside its own key only: a's 1001 is under orderNo, not customer.
        Assert.Equal(Ids(b), await Search(scope, inDoc, "PromotedProperty:4:customer:1001"));
        // Each turns round on its own.
        Assert.Equal(Ids(c), await Search(scope, inDoc, "PromotedProperty:2:orderNo:1001", "PromotedProperty:4:customer:acme"));
        Assert.Equal(Ids(a, c), await Search(scope, inDoc, "PromotedProperty:1:orderNo", "PromotedProperty:2:customer:zed"));
        Assert.Equal(Ids(none), await Search(scope, inDoc, "PromotedProperty:2:orderNo"));
        // A value with a colon in it is still one value: only the first colon ends the key.
        Assert.Empty(await Search(scope, inDoc, "PromotedProperty:4:orderNo:SO:1"));

        await Assert.ThrowsAsync<SWValidationException>(() => Search(scope, inDoc, "PromotedProperty:4::acme"));
    }

    // ------------------------------------------------------------------- helpers

    private static (BitweenDbContext Db, XchangeService Xs) Services(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), scope.ServiceProvider.GetRequiredService<XchangeService>());

    private static async Task<Document> NewDocument(BitweenDbContext db, string name)
    {
        var doc = new Document(null, $"{name} {Guid.NewGuid():N}"[..40], DocumentFormat.Json);
        db.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private static async Task<Subscription> NewSubscription(BitweenDbContext db, string name, int documentId, int? partnerId = null)
    {
        var sub = partnerId is { } partner
            ? new Subscription(name, documentId, SubscriptionType.Internal, partner) { Inactive = false }
            : new Subscription(name, documentId) { Inactive = false };
        db.Add(sub);
        await db.SaveChangesAsync();
        return sub;
    }

    private static string[] Ids(params object[] exchanges) =>
        exchanges.Select(x => x is Xchange xchange ? xchange.Id : (string)x).Order().ToArray();

    private static async Task<string[]> Search(AsyncServiceScope scope, params string[] filters)
    {
        var (db, xs) = Services(scope);
        var response = (SearchyResponse<XchangeRow>)await new Resources.Xchanges.Search(db, xs, scope.Superuser())
            .Handle(new SearchyRequest(filters) { PageSize = 50 });
        Assert.Equal(response.Result.Count(), response.TotalCount);
        return response.Result.Select(r => r.Id).Order().ToArray();
    }

    /// <summary>What "select all matching" acts on for the same filters, as bulk retry and export resolve it.</summary>
    private static async Task<string[]> Select(AsyncServiceScope scope, params string[] filters)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var filter = string.Join("&", filters.Select(f => "filter=" + Uri.EscapeDataString(f)));
        return (await new XchangeSelection(db).Resolve(new XchangeFilesExport { Filter = filter }, 100)).Order().ToArray();
    }

    private static async Task<XchangeSummary> Summarise(AsyncServiceScope scope, params string[] filters)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return (XchangeSummary)await new Summary(db, scope.Superuser()).Handle(new SearchyRequest(filters));
    }
}
