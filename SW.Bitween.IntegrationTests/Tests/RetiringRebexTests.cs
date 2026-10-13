using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Adapters;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The built-in Rebex adapters moved to the marketplace: they are offered only while something
/// still uses them, and the open-source ones are offered as before.
/// </summary>
/// <remarks>
/// As in <see cref="RetiringMapperTests"/>, each test asserts both directions, since the answer is
/// a fact about the whole database.
/// </remarks>
[Collection("Bitween")]
public class RetiringRebexTests(BitweenFixture fixture)
{
    const string FtpReceiver = "NativeRebexFtpReceiver";
    const string Pop3Receiver = "NativeRebexPop3Receiver";
    const string Upload = "NativeRebexFtpUploadHandler";
    static readonly string[] Rebex = [FtpReceiver, Pop3Receiver, Upload];

    static int _seq;
    static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>
    /// What a picker of <paramref name="kind"/> is offered, from every built-in adapter of that
    /// kind with a licence key saved. The fixture registers only test adapters, so the list is
    /// given here and the filter the listing applies is asked directly.
    /// </summary>
    async Task<List<string>> Listed(string kind)
    {
        string[] all = kind == "receivers"
            ? [FtpReceiver, Pop3Receiver, "NativeSftpReceiver", "NativePop3Receiver"]
            : [Upload, "NativeSftpUploadHandler"];
        await using var scope = fixture.CreateScope();
        return await all.ExceptRetiring(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
    }

    /// <summary>Runs <paramref name="act"/> with nothing left naming a Rebex adapter.</summary>
    async Task Licensed(System.Func<Task> act)
    {
        await ParkExistingUsers();
        await act();
    }

    async Task ParkExistingUsers()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var lowered = Rebex.Select(r => r.ToLower()).ToList();
        foreach (var s in await db.Set<Subscription>().Where(s => lowered.Contains(s.ReceiverId.ToLower()) || lowered.Contains(s.HandlerId.ToLower())).ToListAsync())
        {
            if (lowered.Contains(s.ReceiverId?.ToLower())) s.ReceiverId = null;
            if (lowered.Contains(s.HandlerId?.ToLower())) s.HandlerId = null;
        }
        foreach (var n in await db.Set<Notifier>().Where(n => lowered.Contains(n.HandlerId.ToLower())).ToListAsync())
            n.HandlerId = null;
        await db.Set<NotificationChannel>().Where(c => lowered.Contains(c.HandlerId.ToLower())).ExecuteDeleteAsync();
        foreach (var p in await db.Set<RetryPolicy>().Where(p => lowered.Contains(p.AlertHandlerId.ToLower())).ToListAsync())
            p.AlertHandlerId = null;
        await db.SaveChangesAsync();
    }

    async Task<int> SubscriptionWith(string? receiverId = null, string? handlerId = null, bool inactive = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Rebex doc"), DocumentFormat.Json);
        db.Set<Document>().Add(document);
        var partner = new Partner(Unique("Rebex partner"));
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();
        var subscription = new Subscription(Unique("Rebex sub"), document.Id, SubscriptionType.Internal, partner.Id)
        {
            ReceiverId = receiverId, HandlerId = handlerId, Inactive = inactive,
        };
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    async Task Forget(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var s = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        s.ReceiverId = null;
        s.HandlerId = null;
        await db.SaveChangesAsync();
    }

    [Fact]
    public Task A_Rebex_receiver_is_listed_only_while_a_subscription_still_uses_it() => Licensed(async () =>
    {
        var receivers = await Listed("receivers");
        Assert.DoesNotContain(FtpReceiver, receivers);
        Assert.DoesNotContain(Pop3Receiver, receivers);
        Assert.Contains("NativeSftpReceiver", receivers);
        Assert.Contains("NativePop3Receiver", receivers);

        // An inactive one still counts: it can be switched back on, and its picker has to show it.
        var id = await SubscriptionWith(receiverId: "nativerebexftpreceiver", inactive: true);
        receivers = await Listed("receivers");
        Assert.Contains(FtpReceiver, receivers);
        Assert.DoesNotContain(Pop3Receiver, receivers);

        await Forget(id);
        Assert.DoesNotContain(FtpReceiver, await Listed("receivers"));
    });

    [Fact]
    public Task The_Rebex_upload_handler_counts_a_notification_channel_as_using_it() => Licensed(async () =>
    {
        Assert.DoesNotContain(Upload, await Listed("handlers"));

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Set<NotificationChannel>().Add(new NotificationChannel { Name = Unique("Rebex channel"), HandlerId = Upload });
            await db.SaveChangesAsync();
        }
        Assert.Contains(Upload, await Listed("handlers"));

        await ParkExistingUsers();
        Assert.DoesNotContain(Upload, await Listed("handlers"));
    });

    [Fact]
    public Task The_Rebex_upload_handler_counts_a_notifier_as_using_it() => Licensed(async () =>
    {
        var handlers = await Listed("handlers");
        Assert.DoesNotContain(Upload, handlers);
        Assert.Contains("NativeSftpUploadHandler", handlers);

        var id = await SubscriptionWith(handlerId: Upload);
        Assert.Contains(Upload, await Listed("handlers"));
        await Forget(id);

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Set<Notifier>().Add(new Notifier(Unique("Rebex notifier")) { HandlerId = Upload });
            await db.SaveChangesAsync();
        }
        Assert.Contains(Upload, await Listed("handlers"));

        await ParkExistingUsers();
        Assert.DoesNotContain(Upload, await Listed("handlers"));
    });
}
