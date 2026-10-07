using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMQ.Client;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bus;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Source values across the bus: a delivery's response published as a message carries the id of
/// the exchange that delivered beside its body, and a bus gateway route reading that message reads
/// what its subscription uses out of that exchange's input, the original document.
/// </summary>
[Collection("Bitween")]
public class BusSourceValuesTests(BitweenFixture fixture)
{
    private const string Responder = nameof(Adapters.NativeTestResponder);

    [Fact]
    public async Task A_response_published_on_the_bus_carries_the_delivering_exchange_beside_the_body()
    {
        var message = Unique("order-acked").Replace(" ", "");
        var sourceId = await Publisher(message);

        // Nothing in the fixture consumes the bus, so a queue of the test's own catches the message.
        using var connection = await Broker();
        using var channel = connection.CreateModel();
        var queue = channel.QueueDeclare().QueueName;
        await using (var scope = fixture.CreateScope())
            channel.QueueBind(queue, scope.ServiceProvider.GetRequiredService<BusOptions>().ProcessExchange,
                message.ToLower());

        var xchangeId = await Create(sourceId, "{\"order\":{\"number\":\"1001\"}}");
        await Run(xchangeId);

        var got = channel.BasicGet(queue, autoAck: true);
        Assert.NotNull(got);
        // The body is what any other reader of the message has always had.
        Assert.Equal("{\"ack\":true}", Encoding.UTF8.GetString(got.Body.ToArray()));
        var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(
            Encoding.UTF8.GetString((byte[])got.BasicProperties.Headers[RequestContext.ValuesHeaderName]));
        // Only the id travels, however much a route reads, so it can't grow past what the bus takes.
        Assert.Equal(xchangeId, Assert.Single(values).Value);
        Assert.Equal(StartupValuesFiller.SourceXchangeBusValue, values.Single().Key);
    }

    [Fact]
    public async Task A_bus_gateway_route_reads_its_values_from_the_original_document()
    {
        var route = await Route("http://host/{{source.order.number}}");
        var delivery = await Create(await Publisher(Unique("unused").Replace(" ", "")), "{\"order\":{\"number\":\"SO-9\"}}");
        await Run(delivery);

        var child = await Consume(route, "{\"ack\":true}", delivery);

        Assert.Equal("http://host/SO-9", child.HandlerProperties["Url"]);
        Assert.Equal(delivery, child.SourceXchangeId);
        Assert.Equal("SO-9", child.SourceValues["order.number"]);
        Assert.True((await Run(child.Id)).Success);
    }

    [Fact]
    public async Task A_message_published_without_a_source_fails_a_route_that_uses_one()
    {
        var route = await Route("http://host/{{source.order.number}}");

        // Another product's message, say, or one published before responses carried the id.
        var child = await Consume(route, "{\"ack\":true}", sourceXchangeId: null);
        var result = await Run(child.Id);

        Assert.False(result.Success);
        Assert.Contains("wasn't published as a Bitween delivery's response", result.Exception);
    }

    [Fact]
    public async Task A_message_from_a_delivery_in_another_Bitween_fails_a_route_that_uses_one()
    {
        var route = await Route("http://host/{{source.order.number}}");

        // Another instance on the same bus published it: the id is real there, not here.
        var child = await Consume(route, "{\"ack\":true}", Guid.NewGuid().ToString("N"));
        var result = await Run(child.Id);

        Assert.False(result.Success);
        Assert.Contains("couldn't be read", result.Exception);
    }

    // ---------------------------------------------------------------- arrangement

    private record RouteSetup(int DocumentId, int SubscriptionId, string Message);

    /// <summary>A bus-enabled type with a gateway whose one route runs a subscription calling <paramref name="url"/>.</summary>
    private async Task<RouteSetup> Route(string url)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var message = Unique("acked").Replace(" ", "");
        var doc = new Document(null, Unique("Acknowledgement"), DocumentFormat.Json)
            { BusEnabled = true, BusMessageTypeName = message };
        db.Add(doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Route runs"), doc.Id, SubscriptionType.BusGateway)
            { Inactive = false, HandlerId = Responder };
        subscription.SetDictionaries(Props(("Url", url)), Props(), Props(), Props(), Props());
        db.Add(subscription);
        await db.SaveChangesAsync();

        var gateway = new BusGateway { Name = Unique("Acks"), DocumentId = doc.Id };
        db.Add(gateway);
        await db.SaveChangesAsync();
        db.Add(new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = subscription.Id });
        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return new RouteSetup(doc.Id, subscription.Id, message);
    }

    /// <summary>
    /// Hands the message to the bus consumer as SW-Bus would, with the delivering exchange's id its
    /// publisher sent, and returns the exchange the route started.
    /// </summary>
    private async Task<Xchange> Consume(RouteSetup route, string body, string sourceXchangeId)
    {
        await using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RequestContext>();
            context.Set(null, sourceXchangeId == null
                ? []
                : [new RequestValue(StartupValuesFiller.SourceXchangeBusValue, sourceXchangeId,
                    RequestValueType.ServiceBusValue)]);
            await ActivatorUtilities.CreateInstance<BusService>(scope.ServiceProvider).Process(route.Message, body);
        }

        string parentId;
        await using (var scope = fixture.CreateScope())
            parentId = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
                .AsNoTracking().Where(x => x.DocumentId == route.DocumentId && x.SubscriptionId == null)
                .Select(x => x.Id).SingleAsync();
        await Run(parentId);

        await using (var scope = fixture.CreateScope())
            return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
                .AsNoTracking().SingleAsync(x => x.SubscriptionId == route.SubscriptionId);
    }

    private async Task<string> Create(int subscriptionId, string body)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        var subscription = await db.Set<Subscription>().SingleAsync(s => s.Id == subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile(body), null, Guid.NewGuid().ToString("N"));
        await db.SaveChangesAsync();
        return xchange.Id;
    }

    /// <summary>Processes an exchange already created, and returns its result.</summary>
    private async Task<XchangeResult> Run(string xchangeId)
    {
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using (var scope = fixture.CreateScope())
            return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
    }

    /// <summary>A subscription that delivers and publishes its response as <paramref name="message"/>.</summary>
    private async Task<int> Publisher(string message)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var doc = new Document(null, Unique("Published order"), DocumentFormat.Json);
        db.Add(doc);
        await db.SaveChangesAsync();
        var source = new Subscription(Unique("Publishes"), doc.Id, SubscriptionType.BusGateway)
        {
            Inactive = false, HandlerId = Responder, ResponseMessageTypeName = message,
        };
        source.SetDictionaries(Props(("Body", "{\"ack\":true}")), Props(), Props(), Props(), Props());
        db.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task<IConnection> Broker()
    {
        await using var scope = fixture.CreateScope();
        var url = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ");
        return new ConnectionFactory { Uri = new Uri(url!) }.CreateConnection();
    }

    private static Dictionary<string, string> Props(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";
}
