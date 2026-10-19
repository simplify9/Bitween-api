using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A Bitween refuses to start under an environment name its queues weren't made under, until the
/// change is confirmed by naming it.
/// </summary>
[Collection("Bitween")]
public class BusEnvironmentTests(BitweenFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await ForgetRecordedNameAsync();
        await EnsureInUseAsync();
    }

    public Task DisposeAsync() => ForgetRecordedNameAsync();

    [Fact]
    public async Task The_name_it_starts_with_is_recorded()
    {
        Assert.Null(await CheckAsync("Staging", confirmed: null, formerName: "Staging"));
        Assert.Equal("Staging", await RecordedAsync());
        Assert.Null(await CheckAsync("Staging", confirmed: null, formerName: "never asked"));
    }

    [Fact]
    public async Task A_different_name_is_refused_with_both_names_and_the_way_to_confirm()
    {
        await CheckAsync("Development", confirmed: null, formerName: "Development");

        var problem = await CheckAsync("Production", confirmed: null, formerName: "never asked");

        Assert.Contains("ASPNETCORE_ENVIRONMENT is Production", problem);
        Assert.Contains("back to Development", problem);
        Assert.Contains("Bitween__ConfirmBusEnvironment=Production", problem);
        Assert.Equal("Development", await RecordedAsync());
    }

    [Fact]
    public async Task Confirming_the_new_name_records_it_and_a_later_change_is_refused_again()
    {
        await CheckAsync("Development", confirmed: null, formerName: "Development");

        Assert.Null(await CheckAsync("Production", confirmed: "production", formerName: "never asked"));
        Assert.Equal("Production", await RecordedAsync());

        // Left in place, the confirmation names Production only.
        Assert.NotNull(await CheckAsync("Staging", confirmed: "Production", formerName: "never asked"));
    }

    [Fact]
    public async Task Before_names_were_recorded_the_queues_tell_what_it_ran_as()
    {
        var problem = await CheckAsync("Production", confirmed: null, formerName: "Development");

        Assert.NotNull(problem);
        Assert.Null(await RecordedAsync());
    }

    [Fact]
    public async Task An_unreachable_broker_leaves_the_check_to_a_later_start()
    {
        Assert.Null(await CheckAsync("Production", confirmed: null,
            formerName: () => throw new InvalidOperationException("broker down")));
        Assert.Null(await RecordedAsync());
    }

    [Fact]
    public async Task The_broker_says_which_queues_exist_and_removes_an_idle_one()
    {
        var name = BrokerQueues.QueueName("Staging", "bitween-test", "XchangeService", $"Probe{Guid.NewGuid():N}");
        Assert.StartsWith("v3.staging.bitween-test.xchangeservice.probe", name);

        var connectionString = fixture.App.Services.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ")!;
        await using var broker = await BrokerQueues.ConnectAsync(connectionString);
        Assert.Null(await broker.InspectAsync(name));

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using (var connection = await factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            await channel.QueueDeclareAsync(name, durable: true, exclusive: false, autoDelete: false);
            await channel.BasicPublishAsync("", name, "waiting"u8.ToArray());
        }

        Assert.Equal(1u, (await broker.InspectAsync(name))!.MessageCount);
        Assert.False(await broker.DeleteIfIdleAsync(name));

        await using (var connection = await factory.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
            await channel.QueuePurgeAsync(name);

        Assert.True(await broker.DeleteIfIdleAsync(name));
        Assert.Null(await broker.InspectAsync(name));
    }

    private Task<string?> CheckAsync(string current, string? confirmed, string formerName) =>
        CheckAsync(current, confirmed, () => Task.FromResult(formerName));

    private async Task<string?> CheckAsync(string current, string? confirmed, Func<Task<string>> formerName)
    {
        await using var scope = fixture.CreateScope();
        return await BusEnvironment.Check(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
            current, confirmed!, formerName, NullLogger.Instance);
    }

    private async Task<string?> RecordedAsync()
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Setting>()
            .Where(s => s.Id == BusEnvironment.RecordedKey).Select(s => s.Value).SingleOrDefaultAsync();
    }

    private async Task ForgetRecordedNameAsync()
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Setting>()
            .Where(s => s.Id == BusEnvironment.RecordedKey).ExecuteDeleteAsync();
    }

    /// <summary>The queues are only asked about for an installation already in use.</summary>
    private async Task EnsureInUseAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        if (await db.Set<Subscription>().AnyAsync()) return;

        var document = new Document(null, $"Bus environment {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();
        db.Add(new Subscription($"Bus environment {Guid.NewGuid():N}", document.Id, SubscriptionType.Internal,
            Partner.SystemId));
        await db.SaveChangesAsync();
    }
}
