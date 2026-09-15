using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The audit trail, end to end against a real database: that configuration changes are recorded,
/// that runtime traffic isn't, and — the part worth being sure of — that no credential ever
/// reaches the table.
/// </summary>
[Collection("Bitween")]
public class AuditTrailTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Creating_a_partner_writes_an_audit_entry()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner("Audited Partner");
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var entry = await SingleEntryFor(db, nameof(Partner), partner.Id.ToString());

        Assert.Equal("Added", entry.State);
        Assert.Equal("Audited Partner", Changes(entry)[nameof(Partner.Name)].New);
        Assert.NotEmpty(entry.CorrelationId);
    }

    [Fact]
    public async Task Renaming_records_the_old_and_the_new_value()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner("Before Rename");
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        partner.Name = "After Rename";
        await db.SaveChangesAsync();

        var modified = await EntriesFor(db, nameof(Partner), partner.Id.ToString())
            .Where(e => e.State == "Modified").SingleAsync();

        Assert.Equal("Before Rename", Changes(modified)[nameof(Partner.Name)].Old);
        Assert.Equal("After Rename", Changes(modified)[nameof(Partner.Name)].New);
    }

    /// <summary>
    /// The gap in the hand-written trails this replaces: nothing recorded a deletion, so a
    /// configuration row could vanish leaving no trace of what it had contained.
    /// </summary>
    [Fact]
    public async Task Deleting_records_the_values_the_row_had()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner("Doomed Partner");
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        db.Set<Partner>().Remove(partner);
        await db.SaveChangesAsync();

        var deleted = await EntriesFor(db, nameof(Partner), partner.Id.ToString())
            .Where(e => e.State == "Deleted").SingleAsync();

        Assert.Equal("Doomed Partner", Changes(deleted)[nameof(Partner.Name)].Old);
        Assert.Null(Changes(deleted)[nameof(Partner.Name)].New);
    }

    /// <summary>
    /// Runtime rows outnumber configuration changes by orders of magnitude. If they were audited
    /// the table would be worthless within a day.
    /// </summary>
    [Fact]
    public async Task Runtime_rows_are_not_audited()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var account = new Account("Token Owner", "audit-token@test.local", "hash", AccountRole.Viewer);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        var token = new RefreshToken(account.Id, LoginMethod.EmailAndPassword);
        db.Set<RefreshToken>().Add(token);
        await db.SaveChangesAsync();

        Assert.Empty(await EntriesFor(db, nameof(RefreshToken), token.Id).ToListAsync());
        // the account that owns it is configuration, and is recorded
        Assert.NotEmpty(await EntriesFor(db, nameof(Account), account.Id.ToString()).ToListAsync());
    }

    [Fact]
    public async Task Adapter_properties_never_reach_the_trail()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner("Partner With Secrets")
        {
            AdapterProperties = new Dictionary<string, string>
            {
                ["Host"] = "smtp.example.com",
                ["Password"] = "hunter2-should-never-be-stored"
            }
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var entry = await SingleEntryFor(db, nameof(Partner), partner.Id.ToString());

        Assert.DoesNotContain("hunter2-should-never-be-stored", entry.Changes);
        Assert.DoesNotContain(nameof(Partner.AdapterProperties), entry.Changes);
        // the rest of the entity is still recorded
        Assert.Equal("Partner With Secrets", Changes(entry)[nameof(Partner.Name)].New);
    }

    [Fact]
    public async Task An_account_password_never_reaches_the_trail()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var account = new Account("Audited Account", "audit-account@test.local",
            "hashed-password-should-never-be-stored", AccountRole.Viewer);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        var entry = await SingleEntryFor(db, nameof(Account), account.Id.ToString());

        Assert.DoesNotContain("hashed-password-should-never-be-stored", entry.Changes);
        // Asked of the parsed changes rather than the serialized string: "Password" is a substring
        // of MustChangePassword, which is an ordinary audited flag, so searching the JSON text
        // reported a leak the moment a second property happened to contain the word.
        Assert.DoesNotContain(nameof(Account.Password), Changes(entry).Keys);
        Assert.Equal("Audited Account", Changes(entry)[nameof(Account.DisplayName)].New);
    }

    /// <summary>
    /// Settings are the one place a value's secrecy is known without asking an adapter, so unlike
    /// the adapter property bags they are recorded — except where the catalog says otherwise.
    /// </summary>
    [Fact]
    public async Task A_secret_settings_value_is_redacted_while_a_plain_one_is_kept()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        db.Set<Setting>().Add(new Setting { Id = "Bitween.RebexLicenseKey", Value = "licence-key-should-never-be-stored" });
        db.Set<Setting>().Add(new Setting { Id = "Bitween.AreXChangeFilesPrivate", Value = "true" });
        await db.SaveChangesAsync();

        var secret = await SingleEntryFor(db, nameof(Setting), "Bitween.RebexLicenseKey");
        var plain = await SingleEntryFor(db, nameof(Setting), "Bitween.AreXChangeFilesPrivate");

        Assert.DoesNotContain("licence-key-should-never-be-stored", secret.Changes);
        Assert.DoesNotContain(nameof(Setting.Value), secret.Changes);
        Assert.Equal("true", Changes(plain)[nameof(Setting.Value)].New);
    }

    static IQueryable<AuditEntry> EntriesFor(BitweenDbContext db, string entityName, string key) =>
        db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.EntityName == entityName && e.EntityKey == key)
            .OrderBy(e => e.OccurredOn);

    static async Task<AuditEntry> SingleEntryFor(BitweenDbContext db, string entityName, string key) =>
        await EntriesFor(db, entityName, key).SingleAsync();

    static Dictionary<string, Diff> Changes(AuditEntry entry) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Diff>>(entry.Changes);

    private class Diff
    {
        // Null is the point: a property that had no value before, or none after.
        public string? Old { get; set; }
        public string? New { get; set; }
    }
}
