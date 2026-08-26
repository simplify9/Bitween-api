using System;
using System.Linq;
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

        var document = new Document(null, "Receiving Test Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

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
    public async Task Receiving_job_records_one_attempt_with_the_exchanges_it_created()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var document = new Document(null, "Receiving Attempt Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription("Receive Attempt Test", document.Id);
        subscription.ReceiverId = nameof(NativeTestReceiver);
        subscription.Inactive = false;
        // SetSchedules() throws "Invalid schedule" with none configured — a real Receiving
        // subscription always has one, so give this test one too.
        subscription.SetSchedules(new[] { new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(30)) });
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new ReceivingJobParams(subscription.Id, null));

        var attempt = await db.Set<ReceiveAttempt>().SingleAsync(a => a.SubscriptionId == subscription.Id);
        Assert.Equal(ReceiveOutcome.Received, attempt.Outcome);
        Assert.Null(attempt.ErrorMessage);
        Assert.Equal(2, attempt.ExchangeIds.Length);

        var xchangeIds = await db.Set<Xchange>()
            .Where(x => x.SubscriptionId == subscription.Id)
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Equal(xchangeIds.OrderBy(i => i), attempt.ExchangeIds.OrderBy(i => i));
    }

    [Fact]
    public async Task Receiving_job_records_a_failed_attempt_when_listing_files_throws()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var document = new Document(null, "Receiving Failure Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription("Receive Failure Test", document.Id);
        subscription.ReceiverId = nameof(NativeFailingTestReceiver);
        subscription.Inactive = false;
        subscription.SetSchedules(new[] { new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(30)) });
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new ReceivingJobParams(subscription.Id, null));

        var attempt = await db.Set<ReceiveAttempt>().SingleAsync(a => a.SubscriptionId == subscription.Id);
        Assert.Equal(ReceiveOutcome.Failed, attempt.Outcome);
        Assert.Contains("Connection refused", attempt.ErrorMessage);
        Assert.Empty(attempt.ExchangeIds);
    }

    [Fact]
    public async Task Receiving_job_records_no_new_data_when_nothing_is_found()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var document = new Document(null, "Receiving Empty Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription("Receive Empty Test", document.Id);
        subscription.ReceiverId = nameof(NativeEmptyTestReceiver);
        subscription.Inactive = false;
        subscription.SetSchedules(new[] { new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(30)) });
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        cache.Revoke();

        await job.Execute(new ReceivingJobParams(subscription.Id, null));

        var attempt = await db.Set<ReceiveAttempt>().SingleAsync(a => a.SubscriptionId == subscription.Id);
        Assert.Equal(ReceiveOutcome.NoNewData, attempt.Outcome);
        Assert.Null(attempt.ErrorMessage);
        Assert.Empty(attempt.ExchangeIds);
    }

    [Fact]
    public async Task Receiving_job_does_nothing_for_inactive_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var job = scope.ServiceProvider.GetRequiredService<ReceivingJob>();

        var document = new Document(null, "Inactive Receiving Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

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
