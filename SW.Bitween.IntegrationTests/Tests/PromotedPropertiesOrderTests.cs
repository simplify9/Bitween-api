using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>Promoted properties keep the order their information type lists them in, on every database.</summary>
[Collection("Bitween")]
public class PromotedPropertiesOrderTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Promoted_properties_come_back_in_the_order_the_information_type_lists_them()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Postgres' jsonb would hand these back shortest key first: zeta, alpha.
        var document = new Document(null, Unique("Ordered"), DocumentFormat.Json);
        document.SetDictionaries(new Dictionary<string, string> { ["alpha"] = "$.a", ["zeta"] = "$.z" });
        db.Add(document);
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var xchange = await service.CreateXchange(document, null, new XchangeFile("{\"a\":1,\"z\":2}"));
        var filtered = new FilterResult();
        filtered.Properties["zeta"] = "2";
        filtered.Properties["alpha"] = "1";
        db.Add(new XchangePromotedProperties(xchange.Id, filtered));
        await db.SaveChangesAsync();

        await using var readScope = fixture.CreateScope();
        var stored = await readScope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Document>().AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Equal(["alpha", "zeta"], stored.PromotedProperties.Keys.ToArray());

        readScope.Superuser();
        var search = ActivatorUtilities.CreateInstance<Resources.Xchanges.Search>(readScope.ServiceProvider);
        var page = (SearchyResponse<XchangeRow>)await search.Handle(
            new SearchyRequest(new[] { $"Id:1:{xchange.Id}" }), false, null);
        Assert.Equal(["alpha", "zeta"], page.Result.Single().PromotedProperties.Keys.ToArray());
    }

    private static string Unique(string label) => $"{label}-{Guid.NewGuid():N}"[..20];
}
