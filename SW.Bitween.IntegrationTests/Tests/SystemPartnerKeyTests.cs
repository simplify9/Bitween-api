using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The SYSTEM partner's API key the migrations seed, published in this repository. Each test runs
/// inside a transaction that is rolled back, because the SYSTEM partner is shared by the whole
/// collection.
/// </summary>
[Collection("Bitween")]
public class SystemPartnerKeyTests(BitweenFixture fixture)
{
    private const string Configured = "configured-system-key-0123456789abcdef";

    // Stored either way: hashed by the startup pass, or as seeded on a database it has not run on.
    private static bool IsPublished(string stored) =>
        stored == SystemPartnerKey.PublishedKey || stored == PartnerKeyHash.Of(SystemPartnerKey.PublishedKey);

    private static async Task<string[]> Keys(BitweenDbContext db)
    {
        db.ChangeTracker.Clear();
        var partner = await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == Partner.SystemId);
        return partner.ApiCredentials.Select(c => c.Key).ToArray();
    }

    [Fact]
    public async Task A_configured_key_replaces_the_published_one()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await SystemPartnerKey.Secure(db, Configured, NullLogger.Instance);

        var keys = await Keys(db);
        Assert.Contains(PartnerKeyHash.Of(Configured), keys);
        Assert.DoesNotContain(keys, IsPublished);
    }

    [Fact]
    public async Task Without_one_a_new_installation_gets_a_random_key()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Other tests leave accounts behind; a new installation has only the seeded one.
        await db.Set<Account>().Where(a => a.Id != SeededAdministrator.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Deleted, true));

        await SystemPartnerKey.Secure(db, null, NullLogger.Instance);

        var keys = await Keys(db);
        Assert.Single(keys);
        Assert.False(IsPublished(keys[0]));
        Assert.True(PartnerKeyHash.IsHashed(keys[0]));
    }

    [Fact]
    public async Task Without_one_an_existing_installation_keeps_the_published_key()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        db.Set<Account>().Add(new Account("Other", $"other-{Guid.NewGuid():N}@test.local", "hash", AccountRole.Member));
        await db.SaveChangesAsync();

        await SystemPartnerKey.Secure(db, null, NullLogger.Instance);

        Assert.Contains(await Keys(db), IsPublished);
    }

    [Fact]
    public async Task The_published_key_is_refused_as_the_configured_one()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SystemPartnerKey.Secure(db, SystemPartnerKey.PublishedKey, NullLogger.Instance));
    }
}
