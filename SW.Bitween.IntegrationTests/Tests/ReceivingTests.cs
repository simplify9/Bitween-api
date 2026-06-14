using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class ReceivingTests
{
    private readonly BitweenFixture _fixture;

    public ReceivingTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Receiving_job_creates_one_xchange_per_received_file()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var document = new Document(6001, "Receiving Test Doc");
        db.Set<Document>().Add(document);

        // NativeTestReceiver is matched by class name, which starts with "Native" (case-insensitive "native" prefix)
        var subscription = new Subscription("Receive Test", document.Id);
        subscription.ReceiverId = nameof(NativeTestReceiver);
        subscription.Inactive = false;
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        // Invalidate cache so XchangeService can find the newly created subscription
        cache.Revoke();

        await job.Execute(new ReceivingJobParams(subscription.Id, null));

        // NativeTestReceiver.ListFiles() returns 2 files → 2 Xchanges expected
        var count = await db.Set<Xchange>().CountAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Receiving_job_does_nothing_for_inactive_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();

        var document = new Document(6002, "Inactive Receiving Doc");
        db.Set<Document>().Add(document);

        var subscription = new Subscription("Inactive Receiver", document.Id);
        subscription.ReceiverId = nameof(NativeTestReceiver);
        // Inactive = true by default — job should skip it
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        await job.Execute(new ReceivingJobParams(subscription.Id, null));

        var count = await db.Set<Xchange>().CountAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal(0, count);
    }
}
