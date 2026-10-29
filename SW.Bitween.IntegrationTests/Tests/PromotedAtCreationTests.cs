using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// An exchange's promoted properties are read when it is created, while its payload is in hand,
/// and saved in the same batch as the exchange. The worker used to read them, with an insert of
/// its own; it still does for an exchange created by a version that didn't.
/// </summary>
[Collection("Bitween")]
public class PromotedAtCreationTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Promoted_properties_are_there_before_the_exchange_is_processed_and_only_once_after()
    {
        var subscriptionId = await NewSubscriptionAsync();
        var xchangeId = await SubmitAsync(subscriptionId, "{\"order\":{\"number\":\"PO-77\"}}");

        Assert.Equal("PO-77", (await PromotedAsync(xchangeId))?.Properties["Order"]);

        await ProcessAsync(xchangeId);

        Assert.True(await SucceededAsync(xchangeId));
        Assert.Equal("PO-77", (await PromotedAsync(xchangeId))?.Properties["Order"]);
    }

    [Fact]
    public async Task An_exchange_created_without_them_gets_them_when_it_is_processed()
    {
        var subscriptionId = await NewSubscriptionAsync();
        var xchangeId = await SubmitAsync(subscriptionId, "{\"order\":{\"number\":\"PO-78\"}}");

        // As an exchange made by the previous version, which left them to the worker.
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangePromotedProperties>()
                .Where(p => p.Id == xchangeId).ExecuteDeleteAsync();

        await ProcessAsync(xchangeId);

        Assert.True(await SucceededAsync(xchangeId));
        Assert.Equal("PO-78", (await PromotedAsync(xchangeId))?.Properties["Order"]);
    }

    private async Task<int> NewSubscriptionAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Promoted at creation {Guid.NewGuid():N}", DocumentFormat.Json);
        document.SetDictionaries(new Dictionary<string, string> { ["Order"] = "$.order.number" });
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Promoted at creation {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false, HandlerId = nameof(NativeTestResponder) };
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    private async Task<string> SubmitAsync(int subscriptionId, string payload)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .SubmitSubscriptionXchange(subscriptionId, new XchangeFile(payload));
    }

    private async Task ProcessAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .Process("PromotedAtCreation", JsonConvert.SerializeObject(new { Id = xchangeId }));
    }

    private async Task<XchangePromotedProperties> PromotedAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangePromotedProperties>()
            .AsNoTracking().SingleOrDefaultAsync(p => p.Id == xchangeId);
    }

    private async Task<bool> SucceededAsync(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.True(result.Success, result.Exception);
        return result.Success;
    }
}
