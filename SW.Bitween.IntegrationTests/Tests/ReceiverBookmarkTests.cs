using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>A receiver that keeps a bookmark: handed the last good run's, and only moved by a good run.</summary>
[Collection("Bitween")]
public class ReceiverBookmarkTests(BitweenFixture fixture)
{
    static int _seq;

    async Task<int> Subscription(string receiver, Dictionary<string, string> settings)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var name = $"Bookmark {Interlocked.Increment(ref _seq)} {Guid.NewGuid():N}";
        var document = new Document(null, name, DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        var subscription = new Subscription(name, document.Id) { ReceiverId = receiver, Inactive = false };
        subscription.SetSchedules([new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(30))]);
        subscription.SetDictionaries(new Dictionary<string, string>(), new Dictionary<string, string>(), settings, new Dictionary<string, string>(), new Dictionary<string, string>());
        db.Add(subscription);
        await db.SaveChangesAsync();
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    async Task<int> Run(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReceivingJob>().Execute(new ReceivingJobParams(subscriptionId, null));
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>().CountAsync(x => x.SubscriptionId == subscriptionId);
    }

    async Task Items(int subscriptionId, string items, string failOn = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var s = await db.Set<Subscription>().SingleAsync(x => x.Id == subscriptionId);
        var settings = new Dictionary<string, string> { ["Items"] = items };
        if (failOn != null) settings["FailOn"] = failOn;
        s.SetDictionaries(new Dictionary<string, string>(), new Dictionary<string, string>(), settings, new Dictionary<string, string>(), new Dictionary<string, string>());
        await db.SaveChangesAsync();
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
    }

    async Task<string> Saved(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var answer = await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Bookmark>(scope.ServiceProvider)
            .Handle(new Resources.Subscriptions.SubscriptionBookmarkRequest { SubscriptionId = subscriptionId });
        return (string)JObject.FromObject(answer)["Value"];
    }

    [Fact]
    public async Task Each_run_is_handed_the_last_good_run_s_bookmark_and_takes_only_what_is_new()
    {
        var id = await Subscription(nameof(NativeBookmarkTestReceiver), new() { ["Items"] = "1,2,3" });

        Assert.Equal(3, await Run(id));
        Assert.Null(NativeBookmarkTestReceiver.LastGiven);
        Assert.Equal("3", await Saved(id));

        // Nothing new: nothing taken, and the bookmark stays.
        Assert.Equal(3, await Run(id));
        Assert.Equal("3", NativeBookmarkTestReceiver.LastGiven);

        await Items(id, "1,2,3,4,5");
        Assert.Equal(5, await Run(id));
        Assert.Equal("5", await Saved(id));
    }

    [Fact]
    public async Task A_run_in_which_an_item_failed_doesn_t_move_the_bookmark()
    {
        var id = await Subscription(nameof(NativeBookmarkTestReceiver), new() { ["Items"] = "1,2" });
        await Run(id);

        await Items(id, "1,2,3,4,5", failOn: "4");
        Assert.Equal(4, await Run(id)); // 3 and 5 were taken, 4 wasn't
        Assert.Equal("2", await Saved(id));

        // Once 4 can be read, the next run takes the rest again, and moves on.
        await Items(id, "1,2,3,4,5");
        Assert.Equal(7, await Run(id));
        Assert.Equal("5", await Saved(id));
    }

    [Fact]
    public async Task Forgetting_it_starts_the_next_run_from_the_beginning_and_is_in_the_trail()
    {
        var id = await Subscription(nameof(NativeBookmarkTestReceiver), new() { ["Items"] = "1,2" });
        await Run(id);
        Assert.Equal("2", await Saved(id));

        await using (var scope = fixture.CreateScope())
        {
            scope.Superuser();
            await ActivatorUtilities.CreateInstance<Resources.Subscriptions.ForgetBookmark>(scope.ServiceProvider)
                .Handle(id, new Resources.Subscriptions.ForgetSubscriptionBookmark());
            Assert.True(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OperatorAction>()
                .AnyAsync(a => a.Action == OperatorAction.ForgetBookmark && a.Target == $"subscription {id}"));
        }
        Assert.Null(await Saved(id));

        Assert.Equal(4, await Run(id));
        Assert.Null(NativeBookmarkTestReceiver.LastGiven);
    }

    [Fact]
    public async Task A_receiver_that_keeps_none_is_handed_none_and_nothing_is_kept()
    {
        var id = await Subscription(nameof(NativeTestReceiver), new());
        Assert.Equal(2, await Run(id));
        Assert.Null(await Saved(id));
        await using var scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<AdapterState>()
            .AnyAsync(s => s.InstanceKey == $"subscription:{id}"));
    }

    static byte[] Package(string id, Dictionary<string, int> contracts)
    {
        var manifest = new AdapterManifest
        {
            Id = id, Version = "1.0.0", Kinds = ["receiver"], Runtime = AdapterManifest.PythonRuntime, Entry = "main.py",
            DisplayName = "Test receiver", Summary = "For tests.", Contracts = contracts,
            Compatibility = new AdapterCompatibility { Applications = new() { ["bitween"] = "1.0.0" } },
        };
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry(AdapterManifest.FileName).Open())) w.Write(manifest.ToJson());
            using (var w = new StreamWriter(archive.CreateEntry("main.py").Open())) w.Write("print('hello')\n");
        }
        return zip.ToArray();
    }

    [Fact]
    public async Task A_package_keeps_a_bookmark_when_it_declares_the_contract()
    {
        var keeps = $"s9.keeps{Interlocked.Increment(ref _seq)}{Guid.NewGuid().ToString("N")[..6]}";
        var doesnt = keeps + "x";
        await using var scope = fixture.CreateScope();
        var workshop = scope.ServiceProvider.GetRequiredService<AdapterWorkshop>();
        await workshop.UploadAsync(new MemoryStream(Package(keeps, new() { ["bitween"] = 1, ["bitween-bookmark"] = 1 })), "1.0.0", true, null, "test");
        await workshop.UploadAsync(new MemoryStream(Package(doesnt, new() { ["bitween"] = 1 })), "1.0.0", true, null, "test");

        var bookmarks = scope.ServiceProvider.GetRequiredService<ReceiverBookmarks>();
        Assert.True(await bookmarks.KeptByAsync(keeps));
        Assert.True(await bookmarks.KeptByAsync(keeps + "/1.0.0"));
        Assert.False(await bookmarks.KeptByAsync(doesnt));
        Assert.True(await bookmarks.KeptByAsync(nameof(NativeBookmarkTestReceiver)));
        Assert.False(await bookmarks.KeptByAsync(nameof(NativeTestReceiver)));
    }
}
