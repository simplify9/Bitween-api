using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Information types — the kinds of document that flow through Bitween, and the shape every
/// integration is configured against.
/// </summary>
/// <remarks>
/// Almost every rule here is about names that look distinct to a person but are the same thing to
/// a machine. Two types called "Invoice" and "invoice" are indistinguishable in every list that
/// shows them; two publishing as "OrderPlaced" and "orderplaced" are one message on the wire,
/// because the routing key is lower-cased at both ends. Neither produces an error at the moment it
/// is created — the damage shows up later as messages arriving somewhere nobody meant them to.
/// </remarks>
[Collection("Bitween")]
public class InformationTypeTests
{
    private readonly BitweenFixture _fixture;

    public InformationTypeTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<int> Create(DocumentCreate model)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Documents.Create>(scope.ServiceProvider);
        return (int)await handler.Handle(model);
    }

    private async Task Update(int id, DocumentUpdate model)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Documents.Update>(scope.ServiceProvider);
        await handler.Handle(id, model);
    }

    private async Task<Document> Stored(int id)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Document>().AsNoTracking().SingleAsync(d => d.Id == id);
    }

    [Fact]
    public async Task Two_types_cannot_share_a_name_even_in_a_different_case()
    {
        var name = Unique("Invoice");
        await Create(new DocumentCreate { Name = name });

        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Create(new DocumentCreate { Name = name.ToUpperInvariant() }));

        // Allowing both leaves a list nobody can read, and then neither can be saved again:
        // Update refuses the name it already has.
        Assert.StartsWith("NAME_TAKEN", ex.Message);
    }

    [Fact]
    public async Task Two_types_cannot_publish_under_the_same_bus_name_in_a_different_case()
    {
        var busName = Unique("OrderPlaced");
        await Create(new DocumentCreate
        {
            Name = Unique("Bus original"),
            BusEnabled = true,
            BusMessageTypeName = busName,
        });

        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(new DocumentCreate
        {
            Name = Unique("Bus duplicate"),
            BusEnabled = true,
            BusMessageTypeName = busName.ToLowerInvariant(),
        }));

        // The one that actually bites: the routing key is lower-cased by both the publisher and
        // the consumer, so these are one message. Letting both exist meant every message published
        // under either name reached both gateways, with nothing anywhere saying so.
        Assert.StartsWith("DUPLICATED_BUS_TYPE_NAME", ex.Message);
    }

    [Fact]
    public async Task A_code_is_claimed_once()
    {
        var code = "INV_" + Interlocked.Increment(ref _seq);
        await Create(new DocumentCreate { Name = Unique("Coded"), Code = code });

        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Create(new DocumentCreate { Name = Unique("Coded again"), Code = code }));

        Assert.StartsWith("CODE_TAKEN", ex.Message);
    }

    [Fact]
    public async Task Renaming_a_type_actually_persists()
    {
        var id = await Create(new DocumentCreate { Name = Unique("Before rename") });
        var newName = Unique("After rename");
        var newCode = "REN_" + Interlocked.Increment(ref _seq);

        await Update(id, new DocumentUpdate { Name = newName, Code = newCode });

        // Name and Code have private setters, and the bulk property copy only writes public ones —
        // so the rename used to be accepted, reported as saved, and silently discarded. The handler
        // now sets both explicitly; this is here so that cannot come back.
        var stored = await Stored(id);
        Assert.Equal(newName, stored.Name);
        Assert.Equal(newCode, stored.Code);
    }

    [Fact]
    public async Task A_type_can_keep_its_own_name_when_something_else_is_edited()
    {
        var name = Unique("Keeps its name");
        var id = await Create(new DocumentCreate { Name = name });

        // The uniqueness check has to exclude the row being edited, or the second save of any
        // type fails on the name it already has.
        await Update(id, new DocumentUpdate { Name = name, DuplicateInterval = 60 });

        Assert.Equal(60, (await Stored(id)).DuplicateInterval);
    }

    [Fact]
    public async Task Sending_no_promoted_properties_clears_them_rather_than_failing()
    {
        var id = await Create(new DocumentCreate
        {
            Name = Unique("Had properties"),
            PromotedProperties = [new KeyAndValue { Key = "country", Value = "country" }],
        });
        Assert.Single((await Stored(id)).PromotedProperties);

        // An absent list means none. Left implicit this threw ArgumentNullException — a 500 for a
        // request whose meaning the API had simply never decided.
        await Update(id, new DocumentUpdate { Name = Unique("Now has none") });

        Assert.Empty((await Stored(id)).PromotedProperties);
    }

    [Theory]
    [InlineData("$.order.total")]
    [InlineData("order.total")]
    [InlineData("items[0].sku")]
    public async Task A_usable_json_path_is_accepted(string path)
    {
        var id = await Create(new DocumentCreate
        {
            Name = Unique("Json paths"),
            DocumentFormat = DocumentFormat.Json,
            PromotedProperties = [new KeyAndValue { Key = "value", Value = path }],
        });

        Assert.Equal(path, (await Stored(id)).PromotedProperties["value"]);
    }

    [Fact]
    public async Task A_promoted_property_that_could_never_match_is_refused()
    {
        // These are read against every payload that arrives. A path that cannot resolve does not
        // fail loudly — the property is simply blank on every exchange, and on every screen.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(new DocumentCreate
        {
            Name = Unique("Bad path"),
            DocumentFormat = DocumentFormat.Json,
            PromotedProperties = [new KeyAndValue { Key = "total", Value = "not a path!" }],
        }));
        Assert.StartsWith("INVALID_PROMOTED_PROPERTY_PATH", ex.Message);

        var blank = await Assert.ThrowsAsync<SWValidationException>(() => Create(new DocumentCreate
        {
            Name = Unique("Blank path"),
            PromotedProperties = [new KeyAndValue { Key = "total", Value = "  " }],
        }));
        Assert.StartsWith("INVALID_PROMOTED_PROPERTY_VALUE", blank.Message);
    }

    [Fact]
    public async Task The_same_promoted_key_cannot_be_defined_twice()
    {
        // The pair collapses into one dictionary entry, so the second silently wins and the
        // configuration on screen is not the one being used.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Create(new DocumentCreate
        {
            Name = Unique("Duplicate keys"),
            PromotedProperties =
            [
                new KeyAndValue { Key = "country", Value = "shipping.country" },
                new KeyAndValue { Key = "Country", Value = "billing.country" },
            ],
        }));

        Assert.StartsWith("DUPLICATE_PROMOTED_PROPERTY_KEY", ex.Message);
    }

    [Fact]
    public async Task Turning_the_bus_off_drops_the_message_name_with_it()
    {
        var id = await Create(new DocumentCreate
        {
            Name = Unique("Bus toggled"),
            BusEnabled = true,
            BusMessageTypeName = Unique("ToggledMessage"),
        });

        await Update(id, new DocumentUpdate { Name = Unique("Bus off"), BusEnabled = false });

        // A name left behind on a disabled type still occupies the namespace, so the next type
        // that wants it is refused for a reason nothing on screen explains.
        var stored = await Stored(id);
        Assert.False(stored.BusEnabled);
        Assert.True(string.IsNullOrEmpty(stored.BusMessageTypeName));
    }
}
