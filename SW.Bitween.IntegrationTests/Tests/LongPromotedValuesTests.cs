using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A promoted value far longer than anything meant to be promoted — a path that picked up a notes
/// field, say. Its save used to fail after the delivery, so the bus retried the exchange and
/// delivered it again, and the exchange never got a result.
/// </summary>
[Collection("Bitween")]
public class LongPromotedValuesTests(BitweenFixture fixture)
{
    [Fact]
    public async Task An_exchange_with_a_very_long_promoted_value_finishes_and_stores_it_cut()
    {
        // Random text, which doesn't compress: 5,000 characters of it failed the old index.
        var notes = string.Concat(Enumerable.Range(0, 160).Select(_ => Guid.NewGuid().ToString("N")))[..5000];

        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var document = new Document(null, $"Long notes {Guid.NewGuid():N}", DocumentFormat.Json);
            document.SetDictionaries(new Dictionary<string, string> { ["order"] = "$.order", ["notes"] = "$.notes" });
            db.Add(document);
            await db.SaveChangesAsync();

            var subscription = new Subscription($"Long notes {Guid.NewGuid():N}", document.Id, SubscriptionType.BusGateway)
                { Inactive = false, HandlerId = nameof(Adapters.NativeTestResponder) };
            db.Add(subscription);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();

            var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>().CreateXchange(
                subscription, new XchangeFile(JsonConvert.SerializeObject(new { order = "SO-1001", notes })));
            await db.SaveChangesAsync();
            xchangeId = xchange.Id;
        }

        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var result = await db.Set<XchangeResult>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == xchangeId);
            Assert.NotNull(result);
            Assert.True(result.Success, result.Exception);

            var stored = await db.Set<XchangePromotedProperties>().AsNoTracking().SingleAsync(p => p.Id == xchangeId);
            Assert.Equal("SO-1001", stored.Properties["order"]);
            Assert.Equal(notes[..XchangePromotedProperties.MaxValueLength] + "…", stored.Properties["notes"]);
            Assert.Contains("notes:" + notes[..XchangePromotedProperties.MaxValueLength] + "…", stored.PropertiesRaw);

            // The Exchanges page's property search still finds it, by the part that was kept only.
            scope.Superuser();
            var page = (SearchyResponse<XchangeRow>)await ActivatorUtilities
                .CreateInstance<Resources.Xchanges.Search>(scope.ServiceProvider)
                .Handle(new SearchyRequest(new[] { $"PromotedPropertiesRaw:4:{notes[100..140]}" }), false, null);
            Assert.Contains(page.Result, r => r.Id == xchangeId);

            var cutPage = (SearchyResponse<XchangeRow>)await ActivatorUtilities
                .CreateInstance<Resources.Xchanges.Search>(scope.ServiceProvider)
                .Handle(new SearchyRequest(new[] { $"PromotedPropertiesRaw:4:{notes[1000..1040]}" }), false, null);
            Assert.DoesNotContain(cutPage.Result, r => r.Id == xchangeId);
        }
    }
}
