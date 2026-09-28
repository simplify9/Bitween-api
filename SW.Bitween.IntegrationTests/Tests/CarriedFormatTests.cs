using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// CSV and Other information types: carried, never read.
/// </summary>
/// <remarks>
/// Clients build custom adapters that send whatever format they like, and Bitween has no path
/// syntax for most of them. These types say so: no promoted properties, and no filter that could
/// pass — but everything that asked for the type without a filter still gets every message.
/// JSON types are deliberately left as they were, where a payload the reader can't open reaches
/// nobody; clients run live setups on that, and switching the type is how they opt in.
/// </remarks>
[Collection("Bitween")]
public class CarriedFormatTests(BitweenFixture fixture)
{
    private const string Responder = nameof(Adapters.NativeTestResponder);
    private const string PipeFile = "H|FFSTAT|1\nD|1311920360|WC|0.100|KGM\nT|2|1311920|";

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private record Wiring(int DocumentId, int Unfiltered, int Filtered, int RoutedUnfiltered, int RoutedFiltered);

    /// <summary>
    /// A type promoting nothing, with one of each kind of listener: Internal subscriptions and bus
    /// routes, with and without a filter. The filters would pass on a JSON payload of
    /// <c>{"country":"JO"}</c>, so a skip means the payload wasn't read — not that it didn't match.
    /// </summary>
    private async Task<Wiring> Arrange(DocumentFormat format)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(null, Unique($"{format} type"), format);
        db.Add(doc);
        await db.SaveChangesAsync();

        var jordan = new OneOfSpec("$.country", ["JO"]);

        var unfiltered = new Subscription(Unique("Unfiltered"), doc.Id, SubscriptionType.Internal, Partner.SystemId)
            { Inactive = false };
        var filtered = new Subscription(Unique("Filtered"), doc.Id, SubscriptionType.Internal, Partner.SystemId)
            { Inactive = false };
        filtered.SetMatchExpression(jordan);
        var routedUnfiltered = new Subscription(Unique("Routed"), doc.Id, SubscriptionType.BusGateway) { Inactive = false };
        var routedFiltered = new Subscription(Unique("Routed filtered"), doc.Id, SubscriptionType.BusGateway) { Inactive = false };
        var gateway = new BusGateway { Name = Unique("Bus"), DocumentId = doc.Id };
        db.AddRange(unfiltered, filtered, routedUnfiltered, routedFiltered, gateway);
        await db.SaveChangesAsync();

        db.AddRange(
            new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = routedUnfiltered.Id },
            new BusGatewayRoute { BusGatewayId = gateway.Id, SubscriptionId = routedFiltered.Id, MatchExpression = jordan });
        await db.SaveChangesAsync();

        return new Wiring(doc.Id, unfiltered.Id, filtered.Id, routedUnfiltered.Id, routedFiltered.Id);
    }

    private async Task<FilterResult> Dispatch(int documentId, string payload)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return await scope.ServiceProvider.GetRequiredService<FilterService>()
            .Filter(documentId, new XchangeFile(payload));
    }

    [Theory]
    [InlineData(DocumentFormat.Csv)]
    [InlineData(DocumentFormat.Other)]
    public async Task A_carried_type_reaches_every_unfiltered_listener_and_no_filtered_one(DocumentFormat format)
    {
        var wiring = await Arrange(format);

        foreach (var payload in new[] { PipeFile, "<Order><Id>1</Id></Order>", "[{\"country\":\"JO\"}]", "{\"country\":\"JO\"}" })
        {
            var result = await Dispatch(wiring.DocumentId, payload);

            Assert.Contains(wiring.Unfiltered, result.Hits);
            Assert.DoesNotContain(wiring.Filtered, result.Hits);
            Assert.Contains(result.GatewayHits, h => h.SubscriptionId == wiring.RoutedUnfiltered);
            Assert.DoesNotContain(result.GatewayHits, h => h.SubscriptionId == wiring.RoutedFiltered);
            Assert.Empty(result.Properties);
        }
    }

    [Fact]
    public async Task A_json_type_still_reaches_nobody_with_a_payload_it_cannot_read()
    {
        // The behaviour clients run today. Changing it would start subscriptions that have never
        // run, on the day they upgrade.
        var wiring = await Arrange(DocumentFormat.Json);

        var result = await Dispatch(wiring.DocumentId, PipeFile);

        Assert.Empty(result.Hits);
        Assert.Empty(result.GatewayHits);

        var readable = await Dispatch(wiring.DocumentId, "{\"country\":\"JO\"}");
        Assert.Contains(wiring.Unfiltered, readable.Hits);
        Assert.Contains(wiring.Filtered, readable.Hits);
    }

    [Theory]
    [InlineData(DocumentFormat.Csv)]
    [InlineData(DocumentFormat.Other)]
    public async Task A_carried_type_cannot_have_promoted_properties(DocumentFormat format)
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => CreateType(new DocumentCreate
        {
            Name = Unique("Promoting"),
            DocumentFormat = format,
            PromotedProperties = [new KeyAndValue { Key = "id", Value = "$.id" }],
        }));
        Assert.StartsWith("PROMOTED_PROPERTIES_NOT_SUPPORTED", ex.Message);

        // Switching an existing type over: refused while it still promotes something, fine once
        // it doesn't.
        var name = Unique("Switching");
        var id = await CreateType(new DocumentCreate
        {
            Name = name,
            PromotedProperties = [new KeyAndValue { Key = "id", Value = "$.id" }],
        });

        var switched = await Assert.ThrowsAsync<SWValidationException>(() => UpdateType(id, new DocumentUpdate
        {
            Name = name,
            DocumentFormat = format,
            PromotedProperties = [new KeyAndValue { Key = "id", Value = "$.id" }],
        }));
        Assert.StartsWith("PROMOTED_PROPERTIES_NOT_SUPPORTED", switched.Message);

        await UpdateType(id, new DocumentUpdate { Name = name, DocumentFormat = format, PromotedProperties = [] });

        await using var scope = fixture.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Document>().AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.Equal(format, stored.DocumentFormat);
    }

    [Fact]
    public async Task A_scheduled_job_on_a_carried_type_still_runs_its_handler()
    {
        int subscriptionId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var doc = new Document(null, Unique("Pipe file type"), DocumentFormat.Other);
            db.Add(doc);
            await db.SaveChangesAsync();

            var job = new Subscription(Unique("Pipe file job"), doc.Id) { Inactive = false, HandlerId = Responder };
            db.Add(job);
            await db.SaveChangesAsync();
            subscriptionId = job.Id;
        }

        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            // The way ReceivingJob hands over each file it picked up.
            xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile(PipeFile, "track702953.dat"));
        }

        await using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using (var scope = fixture.CreateScope())
        {
            var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
            var result = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);

            Assert.True(result.Success, result.Exception);
            // The responder echoes what it was handed, so this is the file reaching the handler whole.
            Assert.Equal(PipeFile, await xchangeService.GetFile(xchangeId, XchangeFileType.Response));
        }
    }

    [Theory]
    [InlineData(DocumentFormat.Json, ".json")]
    [InlineData(DocumentFormat.Xml, ".json")]
    [InlineData(DocumentFormat.Csv, ".csv")]
    [InlineData(DocumentFormat.Other, "")]
    public async Task The_api_gateway_names_its_file_after_the_type_format(DocumentFormat format, string extension)
    {
        var urlName = Unique("carried").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = new Partner(Unique("Gateway caller"));
            partner.SetApiCredentials([new ApiCredential("test", key)]);
            var doc = new Document(null, Unique($"{format} gateway type"), format);
            db.AddRange(partner, doc);
            await db.SaveChangesAsync();

            var subscription = new Subscription(Unique("Gateway sub"), doc.Id, SubscriptionType.GatewayApiCall)
                { Inactive = false };
            db.Add(subscription);
            await db.SaveChangesAsync();

            db.Add(new ApiGateway
            {
                Name = Unique("Gateway"),
                UrlName = urlName,
                Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
            });
            await db.SaveChangesAsync();
        }

        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
                new ClaimsPrincipal(new ClaimsIdentity()),
                [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);

            var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(PipeFile));

            // Accepted(string) is the uri overload, so the id travels as the Location header.
            var accepted = Assert.IsType<AcceptedResult>(await controller.Post($"{urlName}/async"));
            xchangeId = Assert.IsType<string>(accepted.Location);
        }

        await using (var scope = fixture.CreateScope())
        {
            var xchange = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<Xchange>().AsNoTracking().SingleAsync(x => x.Id == xchangeId);
            Assert.Equal($"{urlName}{extension}", xchange.InputName);
        }
    }

    private async Task<int> CreateType(DocumentCreate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        return (int)await ActivatorUtilities.CreateInstance<Resources.Documents.Create>(scope.ServiceProvider)
            .Handle(model);
    }

    private async Task UpdateType(int id, DocumentUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        await ActivatorUtilities.CreateInstance<Resources.Documents.Update>(scope.ServiceProvider).Handle(id, model);
    }
}
