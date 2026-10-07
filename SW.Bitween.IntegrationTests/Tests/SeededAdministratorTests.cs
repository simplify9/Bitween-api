using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The administrator the migrations seed into every installation, with a password published in
/// this repository. Each test runs inside a transaction that is rolled back, because the seeded
/// account is shared by the whole collection.
/// </summary>
[Collection("Bitween")]
public class SeededAdministratorTests(BitweenFixture fixture)
{
    private const string Chosen = "Chosen-At-Install-9!";

    private static Task<Account> Admin(BitweenDbContext db)
    {
        db.ChangeTracker.Clear();
        return db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == SeededAdministrator.Id);
    }

    [Fact]
    public async Task A_configured_password_replaces_the_published_one_and_ends_its_sessions()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        db.Set<RefreshToken>().Add(new RefreshToken(SeededAdministrator.Id, LoginMethod.EmailAndPassword));
        await db.SaveChangesAsync();

        await SeededAdministrator.Secure(db, Chosen, NullLogger.Instance);

        var admin = await Admin(db);
        Assert.True(SecurePasswordHasher.Verify(Chosen, admin.Password));
        Assert.False(admin.MustChangePassword);
        Assert.Empty(db.Set<RefreshToken>().Where(t => t.AccountId == SeededAdministrator.Id));
    }

    [Fact]
    public async Task Without_one_a_new_installation_refuses_to_start()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Other tests leave accounts behind; a new installation has only the seeded one.
        await db.Set<Account>().Where(a => a.Id != SeededAdministrator.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Deleted, true));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeededAdministrator.Secure(db, null, NullLogger.Instance));
        Assert.Contains("Bitween__InitialAdminPassword", refused.Message);
    }

    [Fact]
    public async Task Without_one_an_installation_with_other_accounts_starts_unchanged()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        db.Set<Account>().Add(new Account("Other", $"other-{Guid.NewGuid():N}@test.local", "hash", AccountRole.Member));
        await db.SaveChangesAsync();

        await SeededAdministrator.Secure(db, null, NullLogger.Instance);

        Assert.True(SecurePasswordHasher.Verify(SeededAdministrator.PublishedPassword, (await Admin(db)).Password));
    }

    [Fact]
    public async Task The_published_password_is_refused_as_the_configured_one()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeededAdministrator.Secure(db, SeededAdministrator.PublishedPassword, NullLogger.Instance));
    }

    [Fact]
    public async Task Once_the_password_has_changed_the_configured_one_is_ignored()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var admin = await db.Set<Account>().SingleAsync(a => a.Id == SeededAdministrator.Id);
        admin.SetPassword("Chosen-Long-Ago-9!");
        await db.SaveChangesAsync();

        await SeededAdministrator.Secure(db, Chosen, NullLogger.Instance);

        Assert.True(SecurePasswordHasher.Verify("Chosen-Long-Ago-9!", (await Admin(db)).Password));
    }
}
