using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Subscriptions;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A subscription's history: a revision per save that changed its configuration, secrets left as
/// the sentinel, running state left out.
/// </summary>
[Collection("Bitween")]
public class SubscriptionHistoryTests(BitweenFixture fixture)
{
    private async Task<int> CreateWithHandler(string name)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, "History " + Guid.NewGuid().ToString("N")[..8], DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        scope.Superuser();
        var created = await ActivatorUtilities.CreateInstance<Create>(scope.ServiceProvider).Handle(new SubscriptionCreate
        {
            Name = name,
            DocumentId = document.Id,
            Type = SubscriptionType.GatewayApiCall,
            HandlerId = "NativeSmtpHandler",
            HandlerProperties =
            [
                new KeyAndValue { Key = "Host", Value = "smtp.partner.example" },
                new KeyAndValue { Key = "Subject", Value = "Order received" },
                new KeyAndValue { Key = "Password", Value = "the-real-secret" },
                new KeyAndValue { Key = "Unknown", Value = "kept-hidden" },
            ],
        });
        return (int)created;
    }

    /// <summary>Saves the subscription as it reads, with one change: what a person saving the page does.</summary>
    private async Task Save(int id, Action<SubscriptionUpdate> change)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var read = (SubscriptionGet)await ActivatorUtilities.CreateInstance<Get>(scope.ServiceProvider).Handle(id);
        change(read);
        await ActivatorUtilities.CreateInstance<Update>(scope.ServiceProvider).Handle(id, read);
    }

    private async Task<List<SubscriptionRevisionRow>> History(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (List<SubscriptionRevisionRow>)await ActivatorUtilities
            .CreateInstance<Resources.Subscriptions.History>(scope.ServiceProvider)
            .Handle(new SubscriptionHistoryRequest { SubscriptionId = id });
    }

    [Fact]
    public async Task Each_save_that_changes_something_is_a_revision_naming_what_changed()
    {
        var id = await CreateWithHandler("History " + Guid.NewGuid().ToString("N")[..6]);

        await Save(id, s => s.Name += " renamed");
        await Save(id, _ => { });

        var history = await History(id);
        Assert.Equal(new[] { 2, 1 }, history.Select(r => r.Number));
        Assert.Equal(new[] { "name" }, history[0].Changed);
        Assert.Empty(history[1].Changed);
    }

    [Fact]
    public async Task A_revision_holds_no_secret_and_no_running_state()
    {
        var id = await CreateWithHandler("History secrets " + Guid.NewGuid().ToString("N")[..6]);

        await using var scope = fixture.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<SubscriptionRevision>().AsNoTracking().Where(r => r.SubscriptionId == id).Select(r => r.Snapshot).SingleAsync();

        Assert.DoesNotContain("the-real-secret", snapshot);
        var handler = JObject.Parse(snapshot)["handlerProperties"]!.ToObject<List<KeyAndValue>>()!;
        Assert.Equal("__private__", handler.Single(p => p.Key == "Password").Value);
        Assert.Equal("smtp.partner.example", handler.Single(p => p.Key == "Host").Value);
        // A setting the adapter doesn't describe isn't known to be safe to keep.
        Assert.Equal("__private__", handler.Single(p => p.Key == "Unknown").Value);
        Assert.Null(JObject.Parse(snapshot)["consecutiveFailures"]);
        Assert.Null(JObject.Parse(snapshot)["inactive"]);
    }

    /// <remarks>
    /// Only while the destination is the same: a save that changes a destination setting (a host, a
    /// URL) drops the secrets the sentinel stood for, so a stored password can't be sent somewhere
    /// new. Restoring a revision with another destination asks for them again, as any save does.
    /// </remarks>
    [Fact]
    public async Task Restoring_is_saving_the_revision_and_keeps_the_secret()
    {
        var id = await CreateWithHandler("History restore " + Guid.NewGuid().ToString("N")[..6]);
        await Save(id, s => s.HandlerProperties = s.HandlerProperties
            .Select(p => p.Key == "Subject" ? new KeyAndValue { Key = "Subject", Value = "Order {{ id }}" } : p).ToList());

        // What the admin UI does: the revision's configuration, saved as the subscription.
        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            var revision = JObject.FromObject(await ActivatorUtilities.CreateInstance<Revision>(scope.ServiceProvider)
                .Handle(new SubscriptionRevisionRequest { SubscriptionId = id, Number = 1 }), SubscriptionRevisions.Json);
            var read = (SubscriptionGet)await ActivatorUtilities.CreateInstance<Get>(scope.ServiceProvider).Handle(id);
            var restored = JObject.FromObject(read, SubscriptionRevisions.Json);
            restored.Merge(revision["configuration"]!, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            await ActivatorUtilities.CreateInstance<Update>(scope.ServiceProvider)
                .Handle(id, restored.ToObject<SubscriptionUpdate>(SubscriptionRevisions.Json)!);
        }

        await using var check = fixture.CreateScope();
        var stored = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.Equal("Order received", stored.HandlerProperties["Subject"]);
        Assert.Equal("the-real-secret", stored.HandlerProperties["Password"]);
        Assert.Equal(3, (await History(id)).Count);
    }
}
