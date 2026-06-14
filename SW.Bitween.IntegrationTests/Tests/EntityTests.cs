using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Verifies that EF Core migrations applied correctly and that domain entities
/// can be persisted and retrieved from the real PostgreSQL container.
/// </summary>
[Collection("Bitween")]
public class EntityTests
{
    private readonly BitweenFixture _fixture;

    public EntityTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Can_create_and_read_document()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Use high IDs to avoid PK conflicts with seeded data (AggregationDocumentId = 10001)
        var document = new Document(5001, "Integration Test Doc");
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var loaded = await db.Set<Document>().FirstOrDefaultAsync(d => d.Id == 5001);

        Assert.NotNull(loaded);
        Assert.Equal("Integration Test Doc", loaded.Name);
    }

    [Fact]
    public async Task Can_create_partner()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Partner.Id is auto-generated (ValueGeneratedOnAdd)
        var partner = new Partner("Test Partner");
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        Assert.True(partner.Id > 0, "Id should be assigned by the database");

        var loaded = await db.Set<Partner>().FindAsync(partner.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Test Partner", loaded.Name);
    }

    [Fact]
    public async Task Can_create_receiving_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Create a document for the subscription to reference
        var document = new Document(5002, "Sub Test Doc");
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription("My Receiver", document.Id);
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        Assert.True(subscription.Id > 0);

        var loaded = await db.Set<Subscription>().FindAsync(subscription.Id);

        Assert.NotNull(loaded);
        Assert.Equal("My Receiver", loaded.Name);
        Assert.Equal(SubscriptionType.Receiving, loaded.Type);
        Assert.True(loaded.Inactive, "New receiving subscriptions start inactive");
    }

    [Fact]
    public async Task Seed_data_exists_after_migration()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var systemPartner = await db.Set<Partner>().FindAsync(Partner.SystemId);
        var aggregationDoc = await db.Set<Document>().FindAsync(Document.AggregationDocumentId);

        Assert.NotNull(systemPartner);
        Assert.Equal("SYSTEM", systemPartner.Name);
        Assert.NotNull(aggregationDoc);
    }
}
