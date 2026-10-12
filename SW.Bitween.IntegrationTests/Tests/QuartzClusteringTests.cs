using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz;
using SW.PrimitiveTypes;
using SW.Scheduler;
using SW.Scheduler.EfCore;
using SW.Scheduler.PgSql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Whether a scheduled job runs once per fire or once per replica, answered with two real Quartz
/// schedulers registered the way Startup registers them, sharing one database.
///
/// The question matters because the docs and several comments said Quartz was not clustered, so
/// every replica ran every job. SW.Scheduler's persistent stores turn clustering on unconditionally
/// (8.1.7 already did, and 10.0.0 is pinned now): each node gets its own instance id and fires are
/// claimed through the job store's row locks. A lease on top would not remove duplicates that do not
/// exist; it would skip the fires that the other node happened to claim.
/// </summary>
public sealed class QuartzClusteringTests(ITestOutputHelper output) : IAsyncLifetime
{
    // Quartz keeps the logger factory of the last host started in a static, so a factory disposed
    // with one test's hosts would break the next test's. One that is never disposed avoids that.
    private static readonly ILoggerFactory Logging = LoggerFactory.Create(_ => { });

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly ProbeRuns _runs = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = new PgSql.BitweenDbContext(
            Configure(new DbContextOptionsBuilder<PgSql.BitweenDbContext>()).Options,
            new RequestContext(), new NoPublish());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// What the rest rests on. A scheduler package that stopped clustering would fail here first.
    /// </summary>
    [Fact]
    public async Task The_scheduler_as_Bitween_registers_it_is_clustered()
    {
        using var a = await StartNodeAsync("a");
        using var b = await StartNodeAsync("b");

        var metaA = await (await a.Services.GetRequiredService<ISchedulerFactory>().GetScheduler()).GetMetaData();
        var metaB = await (await b.Services.GetRequiredService<ISchedulerFactory>().GetScheduler()).GetMetaData();

        Assert.True(metaA.JobStoreClustered);
        Assert.True(metaB.JobStoreClustered);
        Assert.NotEqual(metaA.SchedulerInstanceId, metaB.SchedulerInstanceId);

        await a.StopAsync();
        await b.StopAsync();
    }

    [Fact]
    public async Task Two_nodes_never_run_the_same_fire()
    {
        using var a = await StartNodeAsync("a");
        using var b = await StartNodeAsync("b");
        await ScheduleEverySecondAsync(a);

        await Task.Delay(TimeSpan.FromSeconds(10));
        await a.StopAsync();
        await b.StopAsync();

        Assert.True(_runs.All.Count >= 5, $"only {_runs.All.Count} run(s) in ten seconds");
        Assert.True(_runs.RunTwice().Count == 0, Describe());
    }

    /// <summary>
    /// Nothing ties a job to the node that last ran it. A node that finds nothing to claim sleeps
    /// for Quartz's idle wait (30 s) before looking again, so that is how long a handover can take;
    /// a node that dies rather than stops also has its claimed fires recovered once it misses its
    /// check-ins (10 s interval, 20 s threshold).
    /// </summary>
    [Fact]
    public async Task When_the_node_running_a_job_stops_the_other_carries_on()
    {
        using var a = await StartNodeAsync("a");
        using var b = await StartNodeAsync("b");
        await ScheduleEverySecondAsync(a);

        await WaitUntilAsync(() => _runs.All.Count >= 3);
        var busiest = _runs.All.GroupBy(r => r.Node).OrderByDescending(g => g.Count()).First().Key;
        var (stopped, survivor) = busiest == "a" ? (a, "b") : (b, "a");

        await stopped.StopAsync();
        var stoppedAt = DateTime.UtcNow;
        await WaitUntilAsync(() => _runs.All.Count(r => r.Node == survivor && r.At > stoppedAt) >= 3,
            TimeSpan.FromSeconds(45));
        await (survivor == "a" ? a : b).StopAsync();

        var tookOver = _runs.All.Where(r => r.Node == survivor && r.At > stoppedAt).Min(r => r.At);
        output.WriteLine($"{survivor} took over {(tookOver - stoppedAt).TotalSeconds:0.0} s after {busiest} stopped.");
        Assert.True(_runs.RunTwice().Count == 0, Describe());
    }

    [Fact]
    public async Task A_single_node_runs_every_fire()
    {
        using var only = await StartNodeAsync("only");
        await ScheduleEverySecondAsync(only);

        await WaitUntilAsync(() => _runs.All.Count >= 5);
        await only.StopAsync();

        Assert.All(_runs.All, r => Assert.Equal("only", r.Node));
    }

    // ---------------------------------------------------------------- helpers

    private string Describe() =>
        string.Join("; ", _runs.RunTwice());

    private async Task<IHost> StartNodeAsync(string name)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(Logging);
                services.AddScoped<RequestContext>();
                services.AddSingleton<IPublish, NoPublish>();
                services.AddDbContext<PgSql.BitweenDbContext>(c => Configure(c));

                // As Startup registers it for PostgreSQL, scanning only this assembly for jobs.
                services.AddPgSqlScheduler(pg =>
                    {
                        pg.ConnectionString = _postgres.GetConnectionString();
                        pg.Schema = PgSql.BitweenDbContext.Schema;
                    },
                    assemblies: new[] { typeof(ClusterProbeJob).Assembly });
                services.AddSchedulerMonitoring<PgSql.BitweenDbContext>();
            })
            .Build();

        await host.StartAsync();
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        scheduler.ListenerManager.AddJobListener(new ProbeListener(name, _runs));
        return host;
    }

    /// <summary>Retried because the job is registered by a hosted service that may not have run yet.</summary>
    private static async Task ScheduleEverySecondAsync(IHost node)
    {
        await using var scope = node.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await repo.Schedule<ClusterProbeJob>("* * * * * ?");
                return;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.True(condition(), "the job did not run in time");
    }

    private T Configure<T>(T builder) where T : DbContextOptionsBuilder
    {
        var dataSource = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        dataSource.EnableDynamicJson();
        builder.UseSnakeCaseNamingConvention().UseNpgsql(dataSource.Build(), b =>
        {
            b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
            b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
        });
        return builder;
    }
}

/// <summary>One fire: the time it was due, which node ran it and when.</summary>
public sealed record ProbeRun(string Node, DateTime Due, DateTime At);

public sealed class ProbeRuns
{
    private readonly ConcurrentQueue<ProbeRun> _runs = new();

    public void Add(ProbeRun run) => _runs.Enqueue(run);

    public IReadOnlyList<ProbeRun> All => _runs.ToList();

    /// <summary>Fires that more than one run answered, by the time each was due.</summary>
    public IReadOnlyList<string> RunTwice() => All
        .GroupBy(r => r.Due)
        .Where(g => g.Count() > 1)
        .Select(g => $"{g.Key:HH:mm:ss.fff} ran on {string.Join(", ", g.Select(r => r.Node))}")
        .ToList();
}

/// <summary>
/// Records each run of <see cref="ClusterProbeJob"/> with the fire it answered, which the job
/// itself is not told.
/// </summary>
public sealed class ProbeListener(string node, ProbeRuns runs) : IJobListener
{
    public string Name => $"probe-{node}";

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.JobDetail.Key.Group.Contains(nameof(ClusterProbeJob)))
            runs.Add(new ProbeRun(node,
                context.ScheduledFireTimeUtc?.UtcDateTime ?? context.FireTimeUtc.UtcDateTime, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Configured as Bitween's own jobs are.</summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public sealed class ClusterProbeJob : IScheduledJob
{
    public Task Execute() => Task.CompletedTask;
}

/// <summary>The DbContext takes a publisher for its outbox; nothing here publishes.</summary>
public sealed class NoPublish : IPublish
{
    public Task Publish<TMessage>(TMessage message) => Task.CompletedTask;
    public Task Publish(string messageTypeName, string message) => Task.CompletedTask;
    public Task Publish(string messageTypeName, byte[] message) => Task.CompletedTask;
}
