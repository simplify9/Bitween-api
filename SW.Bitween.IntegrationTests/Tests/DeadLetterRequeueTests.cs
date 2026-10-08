using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Resources.Ops;
using SW.Bus.RabbitMqExtensions;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Sending dead-lettered messages back to the queue they failed on, against a real broker: only
/// what was asked for, with a fresh retry count, and never into a queue that isn't there.
/// </summary>
[Collection("Bitween")]
public class DeadLetterRequeueTests(BitweenFixture fixture)
{
    private static int _seq;

    private async Task<IConnection> Broker()
    {
        await using var scope = fixture.CreateScope();
        var url = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ");
        return new ConnectionFactory { Uri = new Uri(url!) }.CreateConnection();
    }

    private DeadLetterQueues Service(params string[] known) =>
        new(DashboardStub.With(known), null!, fixture.CreateScope().ServiceProvider.GetRequiredService<IConfiguration>(),
            NullLogger<DeadLetterQueues>.Instance);

    /// <summary>A lane — main and bad queue — with <paramref name="dead"/> messages dead-lettered.</summary>
    private async Task<string> Lane(int dead, bool withMain = true)
    {
        var main = $"test.deadletters.lane{Interlocked.Increment(ref _seq)}.{Guid.NewGuid():N}";
        using var connection = await Broker();
        using var channel = connection.CreateModel();
        if (withMain) channel.QueueDeclare(main, durable: false, exclusive: false, autoDelete: false);
        channel.QueueDeclare($"{main}.bad", durable: false, exclusive: false, autoDelete: false);
        for (var i = 1; i <= dead; i++)
        {
            var props = channel.CreateBasicProperties();
            props.CorrelationId = $"c{i}";
            props.Headers = new Dictionary<string, object>
            {
                ["x-death"] = new List<object> { new Dictionary<string, object> { ["count"] = 5L } },
                ["exception0"] = "boom",
                ["tenant"] = "acme",
            };
            channel.BasicPublish("", $"{main}.bad", props, Encoding.UTF8.GetBytes($"{{\"n\":{i}}}"));
        }

        return main;
    }

    private async Task<List<BasicGetResult>> Drain(string queue)
    {
        using var connection = await Broker();
        using var channel = connection.CreateModel();
        var got = new List<BasicGetResult>();
        while (channel.BasicGet(queue, autoAck: true) is { } message) got.Add(message);
        return got;
    }

    [Fact]
    public async Task The_oldest_go_back_with_a_fresh_retry_count_and_everything_else_intact()
    {
        var main = await Lane(3);

        Assert.Equal(2, Service().Requeue($"{main}.bad", 2));

        var requeued = await Drain(main);
        Assert.Equal(["{\"n\":1}", "{\"n\":2}"], requeued.Select(m => Encoding.UTF8.GetString(m.Body.ToArray())));
        var headers = requeued[0].BasicProperties.Headers;
        Assert.False(headers.ContainsKey("x-death"));
        Assert.False(headers.ContainsKey("exception0"));
        Assert.Equal("acme", Encoding.UTF8.GetString((byte[])headers["tenant"]));
        Assert.Equal("c1", requeued[0].BasicProperties.CorrelationId);

        Assert.Single(await Drain($"{main}.bad"));
    }

    [Fact]
    public async Task Nothing_moves_when_the_queue_they_failed_on_is_gone()
    {
        var main = await Lane(1, withMain: false);

        Assert.ThrowsAny<OperationInterruptedException>(() => Service().Requeue($"{main}.bad", 10));

        Assert.Single(await Drain($"{main}.bad"));
    }

    [Fact]
    public async Task Only_a_queue_the_bus_lists_as_dead_letters_is_touched()
    {
        var service = Service("v3.test.bitween.xchangeservice.xchangecreated.bad");

        Assert.Equal("v3.test.bitween.xchangeservice.xchangecreated.bad",
            await service.Resolve("V3.TEST.BITWEEN.XCHANGESERVICE.XCHANGECREATED.BAD"));
        Assert.Null(await service.Resolve("some.other.queue"));

        await using var scope = fixture.CreateScope();
        var ctx = scope.Superuser();
        var handler = new RequeueDeadLetters(service,
            scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), ctx);
        await Assert.ThrowsAsync<SWNotFoundException>(() =>
            handler.Handle(new RequeueDeadLettersModel { Queue = "some.other.queue" }));
    }

    [Fact]
    public async Task The_host_can_build_it()
    {
        await using var scope = fixture.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeadLetterQueues>());
    }

    /// <summary>The bus's dashboard, listing just the dead-letter queues a test names.</summary>
    public class DashboardStub : DispatchProxy
    {
        private string[] _queues = [];

        public static IBusDashboardDataService With(string[] queues)
        {
            var stub = Create<IBusDashboardDataService, DashboardStub>();
            ((DashboardStub)(object)stub)._queues = queues;
            return stub;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod!.Name != nameof(IBusDashboardDataService.GetDeadLetterSummaryAsync))
                throw new NotSupportedException(targetMethod.Name);
            var views = _queues.Select(q =>
                new DeadLetterSummaryView("XchangeService", "XchangeCreated", q, 1, null, null, null, default)).ToArray();
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            object result = resultType.IsArray ? views : views.ToList();
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }
    }
}
