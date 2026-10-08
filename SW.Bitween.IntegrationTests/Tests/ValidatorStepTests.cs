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
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A subscription's validator, run by the real serverless runtime on a gateway call: a payload it
/// rejects is refused while the partner is still on the line, with the validator's reasons, and
/// nothing is created; one it accepts goes through.
/// </summary>
[Collection("Bitween")]
public class ValidatorStepTests(BitweenFixture fixture)
{
    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<(string urlName, string key, int subscriptionId)> ValidatedGateway()
    {
        var urlName = Unique("validated").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Validated partner"));
        partner.SetApiCredentials([new ApiCredential("main", key)]);
        var doc = new Document(null, Unique("Validated doc"), DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Validated sub"), doc.Id, SubscriptionType.GatewayApiCall)
        {
            Inactive = false,
            ValidatorId = BitweenFixture.SampleValidatorId,
        };
        db.Add(subscription);
        await db.SaveChangesAsync();

        db.Add(new ApiGateway
        {
            Name = Unique("Validated gateway"),
            UrlName = urlName,
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        return (urlName, key, subscription.Id);
    }

    private async Task<IActionResult> Call(string urlName, string key, string body)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
            new ClaimsPrincipal(new ClaimsIdentity()),
            [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);

        var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return await controller.Post($"{urlName}/async");
    }

    private async Task<int> Exchanges(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().CountAsync(x => x.SubscriptionId == subscriptionId);
    }

    [Fact]
    public async Task A_payload_the_validator_rejects_is_refused_with_its_reasons_and_creates_nothing()
    {
        var (urlName, key, subscriptionId) = await ValidatedGateway();

        var refused = await Assert.ThrowsAsync<SWValidationException>(() =>
            Call(urlName, key, "{\"Id\":50}"));

        var reasons = refused.Validations.ToDictionary(v => v.Key, v => v.Value);
        Assert.Contains("Id", reasons.Keys);
        Assert.Contains("Name", reasons.Keys);
        Assert.Equal(0, await Exchanges(subscriptionId));
    }

    [Fact]
    public async Task A_payload_the_validator_accepts_goes_through()
    {
        var (urlName, key, subscriptionId) = await ValidatedGateway();

        Assert.IsType<AcceptedResult>(await Call(urlName, key, "{\"Id\":3,\"Name\":\"Widget\"}"));
        Assert.Equal(1, await Exchanges(subscriptionId));
    }
}
