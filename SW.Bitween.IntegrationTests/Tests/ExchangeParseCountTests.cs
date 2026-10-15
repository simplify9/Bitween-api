using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;
using Xunit;
using Xunit.Abstractions;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// How many times one synchronous gateway call parses its payload into a tree, from the gateway's
/// schema check to the handler's URL: the number finding 6 of docs/design/processing-performance.md
/// works down. Held exactly per path, so a change that adds a parse fails here, and one that removes
/// a parse says so by failing too until its number is written down.
/// </summary>
/// <remarks>
/// Counted the way production counts it, through the stage tallies into the exchange's metrics row
/// (<c>ExchangeStats.PayloadParses</c>). Like <see cref="ExchangeCommandCountTests"/>, the test plays
/// the worker and the result lane itself while the call waits. Each path has its own information type,
/// subscription, partner and gateway.
/// </remarks>
[Collection("Bitween")]
public class ExchangeParseCountTests(BitweenFixture fixture, ITestOutputHelper output)
{
    public enum Route
    {
        /// <summary>Rules-based mapper, HTTP handler on a plain URL, an object payload.</summary>
        RulesPlainUrl,

        /// <summary>Scriban mapper, HTTP handler on a templated URL, an object payload the partner's values are written into.</summary>
        ScribanTemplatedUrl,

        /// <summary>Scriban mapper, HTTP handler on a plain URL, a root array, which is not enriched.</summary>
        ScribanRootArray,
    }

    /// <summary>
    /// Per path, with and without a schema: the parses before the shared parse, which
    /// <c>Bitween:SharedPayloadParse=false</c> must still give, and the parses with it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before, by stage: the gateway's schema check (with a schema) and its promoted-property read;
    /// then the worker's schema check (with a schema), the enrichment, the mapper's read of its input,
    /// the Scriban mapper's read of its own output, and the HTTP handler's URL model.
    /// </para>
    /// <para>
    /// Shared: one parse per profile in each scope. The gateway's schema check and promoted-property
    /// read share one. In the worker, the schema check, the enrichment and the Scriban model share the
    /// defaults tree; the rules mapper reads the exact one, so the rules path keeps one parse of each.
    /// A payload the enrichment writes into is new text, parsed again for the Scriban model: its
    /// partner and global values are parsed there (a date-like value becomes a date), which a copy of
    /// the tree would not do. The Scriban output and the URL model are other text, or another reader,
    /// and keep their parses.
    /// </para>
    /// </remarks>
    public static readonly TheoryData<Route, bool, int, int> Parses = new()
    {
        { Route.RulesPlainUrl, false, 2, 2 },
        { Route.RulesPlainUrl, true, 4, 3 },
        { Route.ScribanTemplatedUrl, false, 5, 5 },
        { Route.ScribanTemplatedUrl, true, 7, 5 },
        { Route.ScribanRootArray, false, 4, 3 },
        { Route.ScribanRootArray, true, 6, 3 },
    };

    [Theory]
    [MemberData(nameof(Parses))]
    public async Task A_sync_gateway_call_parses_its_payload_as_often_as_its_path_needs(Route route, bool schema, int before, int shared)
    {
        _ = before;
        Assert.Equal(shared, await ParsesOfOneCallAsync(route, schema));
    }

    [Theory]
    [MemberData(nameof(Parses))]
    public async Task With_the_shared_parse_off_every_step_parses_for_itself_as_before(Route route, bool schema, int before, int shared)
    {
        _ = shared;
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        options.SharedPayloadParse = false;
        try
        {
            Assert.Equal(before, await ParsesOfOneCallAsync(route, schema));
        }
        finally
        {
            options.SharedPayloadParse = true;
        }
    }

    private async Task<long> ParsesOfOneCallAsync(Route route, bool schema)
    {
        var (urlName, key, subscriptionId) = await GatewayAsync(route, schema);
        var body = route == Route.ScribanRootArray ? RootArrayPayload : ObjectPayload;

        // Warms the caches and spends the key's last-used write.
        await SyncCallAsync(urlName, key, subscriptionId, body);
        var recorder = fixture.App.Services.GetRequiredService<MetricsRecorder>();
        recorder.Drain();

        var answer = await SyncCallAsync(urlName, key, subscriptionId, body);
        Assert.Equal(200, (answer as ContentResult)?.StatusCode);

        var rows = recorder.Drain().Exchanges.Where(r => r.SubscriptionId == subscriptionId).ToList();
        Assert.Equal(1, rows.Sum(r => r.Total));
        var parses = rows.Sum(r => r.PayloadParses ?? 0);
        output.WriteLine($"Payload parses for one sync gateway call, {route}, schema {schema}: {parses}");
        return parses;
    }

    private const string ObjectPayload =
        "{\"id\":\"A-1\",\"price\":1.10,\"orderedAt\":\"2026-10-14T10:00:00Z\",\"order\":{\"lines\":[{\"sku\":\"S1\",\"qty\":2}]}}";

    private const string RootArrayPayload = "[{\"id\":\"A-1\",\"price\":1.10},{\"id\":\"A-2\",\"price\":2}]";

    private const string ObjectSchema = "{\"type\":\"object\",\"required\":[\"id\"]}";
    private const string ArraySchema = "{\"type\":\"array\"}";

    private const string ScribanTemplate =
        "{ \"orderId\": {{ id | json }}, \"price\": {{ price | json }}, \"partner\": {{ __partner__?.code | json }} }";

    private static readonly string Rules = JsonConvert.SerializeObject(new MappingRules
    {
        Fields =
        [
            new FieldRule { Target = ["orderId"], From = new ValueSource { Kind = ValueSourceKind.Path, Path = "id" } },
            new FieldRule { Target = ["price"], From = new ValueSource { Kind = ValueSourceKind.Path, Path = "price" } },
        ],
    });

    private async Task<IActionResult> SyncCallAsync(string urlName, string key, int subscriptionId, string body)
    {
        var call = Task.Run(async () =>
        {
            await using var scope = fixture.CreateScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
                new ClaimsPrincipal(new ClaimsIdentity()),
                [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);
            var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return await controller.Post($"{urlName}/sync");
        });

        var xchangeId = await WaitForUnprocessedExchangeAsync(subscriptionId, call);
        if (xchangeId == null) return await call;

        var message = JsonConvert.SerializeObject(new
        {
            Id = xchangeId, Input = body,
            AwaitedOn = fixture.App.Services.GetRequiredService<ResultSignals>().Node,
        });
        await using (var worker = fixture.CreateScope())
            await worker.ServiceProvider.GetRequiredService<XchangeService>().Process("parses-test", message);
        if (await ResultEventQueuedAsync(xchangeId))
            await using (var resultLane = fixture.CreateScope())
                await resultLane.ServiceProvider.GetRequiredService<XchangeService>()
                    .Process("parses-test" + XchangeService.ResultQueueSuffix, message);

        return await call;
    }

    private async Task<bool> ResultEventQueuedAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OutboxMessage>().AsNoTracking()
            .AnyAsync(m => m.MessageType.EndsWith(XchangeService.ResultQueueSuffix) && m.Body.Contains(xchangeId));
    }

    private async Task<string> WaitForUnprocessedExchangeAsync(int subscriptionId, Task call)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !call.IsCompleted)
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var id = await db.Set<Xchange>().AsNoTracking()
                .Where(x => x.SubscriptionId == subscriptionId && !db.Set<XchangeResult>().Any(r => r.Id == x.Id))
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
            if (id != null) return id;
            await Task.Delay(50);
        }

        return null;
    }

    private async Task<(string UrlName, string Key, int SubscriptionId)> GatewayAsync(Route route, bool schema)
    {
        var urlName = $"parses-{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString("N");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner($"Parses partner {Guid.NewGuid():N}")
        {
            AdapterProperties = new Dictionary<string, string> { ["code"] = "P1" },
        };
        partner.SetApiCredentials([new ApiCredential("load", key)]);
        var document = new Document(null, $"Parses doc {Guid.NewGuid():N}", DocumentFormat.Json);
        if (schema) document.SetValidationSchema(route == Route.ScribanRootArray ? ArraySchema : ObjectSchema);
        db.AddRange(partner, document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Parses sub {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall)
        {
            Inactive = false,
            MapperId = route == Route.RulesPlainUrl ? nameof(NativeAdapters.Mapper.NativeMapper) : nameof(NativeJSONMapper),
            HandlerId = nameof(NativeParseCountHttpHandler),
        };
        var mapper = route == Route.RulesPlainUrl
            ? new Dictionary<string, string> { [nameof(NativeMapperInput.MappingRules)] = Rules }
            : new Dictionary<string, string> { ["ScribanTemplate"] = ScribanTemplate };
        var handler = new Dictionary<string, string>
        {
            ["Url"] = route == Route.ScribanTemplatedUrl
                ? "https://partner.test/orders/{{ orderId }}?price={{ price }}"
                : "https://partner.test/orders",
        };
        subscription.SetDictionaries(handler, mapper, new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = $"Parses gateway {Guid.NewGuid():N}",
            UrlName = urlName,
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return (urlName, key, subscription.Id);
    }
}
