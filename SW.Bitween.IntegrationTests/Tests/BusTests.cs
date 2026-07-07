using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Verifies basic RabbitMQ connectivity using the real Testcontainer broker.
/// IPublish.Publish(routingKey, json) is the low-level API used throughout the codebase.
/// These tests confirm the AMQP channel is open and messages are accepted.
/// </summary>
[Collection("Bitween")]
public class BusTests
{
    private readonly BitweenFixture _fixture;

    public BusTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IPublish_is_resolvable_from_di()
    {
        await using var scope = _fixture.CreateScope();
        var publish = scope.ServiceProvider.GetRequiredService<IPublish>();

        Assert.NotNull(publish);
    }

    [Fact]
    public async Task Can_publish_message_to_broker()
    {
        await using var scope = _fixture.CreateScope();
        var publish = scope.ServiceProvider.GetRequiredService<IPublish>();

        // Publish a simple JSON payload. The routing key mirrors the pattern used
        // by the Bitween SaveChangesAsync domain-event dispatch.
        var ex = await Record.ExceptionAsync(async () =>
            await publish.Publish("TestEvent", "{\"id\":\"integration-test\"}"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Can_publish_multiple_messages_in_sequence()
    {
        await using var scope = _fixture.CreateScope();
        var publish = scope.ServiceProvider.GetRequiredService<IPublish>();

        for (var i = 0; i < 5; i++)
        {
            await publish.Publish("TestSequenceEvent", $"{{\"seq\":{i}}}");
        }
        // Passes if no exception is thrown — confirms the channel stays open
    }
}
