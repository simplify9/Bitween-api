using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A partner whose call timed out can't tell whether it arrived. Sending it again with the same
/// Idempotency-Key must return the exchange the first call made, not run the integration twice.
/// </summary>
[Collection("Bitween")]
public class IdempotencyKeyTests(BitweenFixture fixture)
{
    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    /// <summary>A gateway with two partners attached, each with a key of its own.</summary>
    private async Task<(string urlName, string key, string otherKey)> Gateway()
    {
        var urlName = Unique("idem").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");
        var otherKey = Guid.NewGuid().ToString("N");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Idem partner"));
        partner.SetApiCredentials([new ApiCredential("main", key)]);
        var other = new Partner(Unique("Idem other partner"));
        other.SetApiCredentials([new ApiCredential("main", otherKey)]);
        var doc = new Document(null, Unique("Idem doc"), DocumentFormat.Json);
        db.AddRange(partner, other, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Idem sub"), doc.Id, SubscriptionType.GatewayApiCall)
            { Inactive = false };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = Unique("Idem gateway"),
            UrlName = urlName,
            Partners =
            [
                new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id },
                new ApiGatewayPartner { PartnerId = other.Id, SubscriptionId = subscription.Id },
            ],
        });
        await db.SaveChangesAsync();
        return (urlName, key, otherKey);
    }

    private async Task<(IActionResult result, IHeaderDictionary headers)> Call(string urlName, string partnerKey,
        string idempotencyKey)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
            new ClaimsPrincipal(new ClaimsIdentity()),
            [new RequestValue("partnerkey", partnerKey, RequestValueType.HttpHeader)]);

        var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        if (idempotencyKey is not null)
            controller.HttpContext.Request.Headers[IdempotencyGuard.Header] = idempotencyKey;
        var result = await controller.Post($"{urlName}/async");
        return (result, controller.HttpContext.Response.Headers);
    }

    private static string XchangeId(IActionResult result) => (string)Assert.IsType<AcceptedResult>(result).Location;

    private async Task<int> XchangeCount(params string[] ids)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().CountAsync(x => ids.Contains(x.Id));
    }

    [Fact]
    public async Task The_same_key_sent_again_returns_the_first_exchange_and_makes_no_second()
    {
        var (urlName, key, _) = await Gateway();
        var idem = Guid.NewGuid().ToString();

        var (first, firstHeaders) = await Call(urlName, key, idem);
        var (second, secondHeaders) = await Call(urlName, key, idem);

        Assert.Equal(XchangeId(first), XchangeId(second));
        Assert.False(firstHeaders.ContainsKey("Idempotent-Replay"));
        Assert.Equal("true", secondHeaders["Idempotent-Replay"]);
        Assert.Equal(1, await XchangeCount(XchangeId(first)));
    }

    [Fact]
    public async Task Calls_without_a_key_or_with_different_keys_each_make_their_own_exchange()
    {
        var (urlName, key, _) = await Gateway();

        var ids = new[]
        {
            XchangeId((await Call(urlName, key, null)).result),
            XchangeId((await Call(urlName, key, null)).result),
            XchangeId((await Call(urlName, key, "a")).result),
            XchangeId((await Call(urlName, key, "b")).result),
        };

        Assert.Equal(4, ids.Distinct().Count());
    }

    [Fact]
    public async Task Two_partners_using_the_same_key_do_not_get_each_others_exchange()
    {
        var (urlName, key, otherKey) = await Gateway();

        var mine = XchangeId((await Call(urlName, key, "order-1")).result);
        var theirs = XchangeId((await Call(urlName, otherKey, "order-1")).result);

        Assert.NotEqual(mine, theirs);
    }

    [Fact]
    public async Task A_key_past_its_window_makes_a_new_exchange()
    {
        var (urlName, key, _) = await Gateway();
        var idem = Guid.NewGuid().ToString();
        var first = XchangeId((await Call(urlName, key, idem)).result);

        await using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<IdempotencyKey>()
                .Where(k => k.Key == idem)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.CreatedOn,
                    DateTime.UtcNow - IdempotencyGuard.Window - TimeSpan.FromMinutes(1)));
        }

        var second = XchangeId((await Call(urlName, key, idem)).result);
        var third = XchangeId((await Call(urlName, key, idem)).result);

        Assert.NotEqual(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public async Task A_key_too_long_to_store_is_refused()
    {
        var (urlName, key, _) = await Gateway();

        var (result, _) = await Call(urlName, key, new string('k', IdempotencyGuard.MaxKeyLength + 1));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task The_older_partner_endpoint_honours_the_key_too()
    {
        var key = Guid.NewGuid().ToString("N");
        int documentId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var partner = new Partner(Unique("Legacy idem partner"));
            partner.SetApiCredentials([new ApiCredential("main", key)]);
            var doc = new Document(null, Unique("Legacy idem doc"), DocumentFormat.Json);
            db.AddRange(partner, doc);
            await db.SaveChangesAsync();
            db.Add(new Subscription(Unique("Legacy idem sub"), doc.Id, SubscriptionType.ApiCall, partner.Id)
                { Inactive = false });
            await db.SaveChangesAsync();
            documentId = doc.Id;
        }

        async Task<string> Post(string idempotencyKey)
        {
            await using var scope = fixture.CreateScope();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
                new ClaimsPrincipal(new ClaimsIdentity()),
            [
                new RequestValue("partnerkey", key, RequestValueType.HttpHeader),
                new RequestValue(IdempotencyGuard.Header, idempotencyKey, RequestValueType.HttpHeader),
            ]);
            var handler = ActivatorUtilities.CreateInstance<Resources.Xchanges.Update>(scope.ServiceProvider);
            var result = (CqApiResult<string>)await handler.Handle(documentId.ToString(), JObject.Parse("{}"));
            return (string)result.Result;
        }

        var first = await Post("legacy-1");
        Assert.Equal(first, await Post("legacy-1"));
        Assert.NotEqual(first, await Post("legacy-2"));
    }
}
