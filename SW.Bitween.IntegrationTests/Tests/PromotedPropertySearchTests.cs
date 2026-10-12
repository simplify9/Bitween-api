using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The Exchanges page's promoted property filter: a key picked from the list and text that has to
/// be found anywhere inside that key's value.
/// </summary>
[Collection("Bitween")]
public class PromotedPropertySearchTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_picked_key_finds_part_of_its_own_value_and_nothing_in_other_keys()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, $"Prop search {Guid.NewGuid():N}"[..24], DocumentFormat.Json);
        document.SetDictionaries(new Dictionary<string, string>
            { ["orderNo"] = "$.o", ["customer"] = "$.c", ["refOrderNo"] = "$.r" });
        db.Add(document);
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        async Task<string> Exchange(params (string Key, string Value)[] properties)
        {
            var xchange = await service.CreateXchange(document, null, new XchangeFile("{}"));
            var filtered = new FilterResult();
            foreach (var (key, value) in properties)
                filtered.Properties[key] = value;
            db.Add(new XchangePromotedProperties(xchange.Id, filtered));
            await db.SaveChangesAsync();
            return xchange.Id;
        }

        var a = await Exchange(("orderNo", "SO-1001-X"), ("customer", "Acme"));
        // "1001" sits after orderNo here, in the next key's value.
        var b = await Exchange(("orderNo", "PO-7"), ("customer", "Zed-1001"));
        // A key whose name ends in the picked one.
        var c = await Exchange(("refOrderNo", "R-1001"));

        await using var readScope = fixture.CreateScope();
        readScope.Superuser();
        async Task<string[]> Found(params string[] filters)
        {
            var page = (SearchyResponse<XchangeRow>)await ActivatorUtilities
                .CreateInstance<Resources.Xchanges.Search>(readScope.ServiceProvider)
                .Handle(new SearchyRequest(filters.Append($"DocumentId:1:{document.Id}").ToArray()), false, null);
            return page.Result.Select(r => r.Id).Order().ToArray();
        }

        string[] Ids(params string[] ids) => ids.Order().ToArray();

        // The middle of the value, which a "orderNo:1001" term never found.
        Assert.Equal(Ids(a), await Found("PromotedPropertyKey:1:orderNo", "PromotedPropertiesRaw:4:1001"));
        Assert.Equal(Ids(a), await Found("PromotedPropertyKey:1:ORDERNO", "PromotedPropertiesRaw:4:01-x"));
        Assert.Equal(Ids(b), await Found("PromotedPropertyKey:1:orderNo", "PromotedPropertiesRaw:4:7"));
        // A key on its own: "carries this property at all".
        Assert.Equal(Ids(a, b), await Found("PromotedPropertyKey:1:orderNo"));
        // No key: anywhere, as before.
        Assert.Equal(Ids(a, b, c), await Found("PromotedPropertiesRaw:4:1001"));
    }
}
