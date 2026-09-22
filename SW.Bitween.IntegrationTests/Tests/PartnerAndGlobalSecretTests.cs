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

/// <summary>
/// A partner property and a global value can be marked secret, which keeps the value out of every
/// response while leaving the <c>{{partner.…}}</c> / <c>{{globals.…}}</c> reference to it usable.
/// </summary>
/// <remarks>
/// Until this existed the two were the only property bags in the app served in clear — a data
/// source masks, a subscription's adapter properties mask, and these did not, which made them the
/// worst place to keep a credential and the only place operators could share one. The flag is
/// declared rather than guessed from the key's name: these values have always been readable, so
/// inferring would have hidden values an operator could see the day before.
/// </remarks>
[Collection("Bitween")]
public class PartnerAndGlobalSecretTests(BitweenFixture fixture)
{
    // ------------------------------------------------------------------ partners

    /// <summary>
    /// The whole point: a locked value does not come back. Get is the only endpoint that ever
    /// served a partner's property values — Search has always sent names alone.
    /// </summary>
    [Fact]
    public async Task A_locked_partner_property_is_never_returned_in_clear()
    {
        var id = await CreatePartnerAsync(
            new Dictionary<string, string> { ["storeId"] = "CR-114", ["apiKey"] = "sk-live-9" },
            secretProperties: ["apiKey"]);

        var row = await GetPartnerAsync(id);

        Assert.Equal("CR-114", row.AdapterProperties["storeId"]);
        Assert.Equal(AdapterSecretProperties.Sentinel, row.AdapterProperties["apiKey"]);
        Assert.DoesNotContain("sk-live-9", string.Join("|", row.AdapterProperties.Values));
    }

    /// <summary>
    /// Nothing is hidden until someone says so. A property whose name reads like a credential is
    /// still served in clear, because it always was — the alternative silently blinds an operator
    /// on the day they upgrade.
    /// </summary>
    [Fact]
    public async Task An_unlocked_property_stays_readable_whatever_it_is_called()
    {
        var id = await CreatePartnerAsync(
            new Dictionary<string, string> { ["password"] = "still-visible" },
            secretProperties: []);

        var row = await GetPartnerAsync(id);

        Assert.Equal("still-visible", row.AdapterProperties["password"]);
    }

    /// <summary>
    /// The round trip that breaks naive masking: read, change one unrelated field, save. The page
    /// sends every property together, so without the merge an edit to the partner's name would
    /// overwrite the api key with the mask, and adapters would start authenticating as
    /// "__private__".
    /// </summary>
    [Fact]
    public async Task Saving_a_masked_property_back_keeps_the_stored_value()
    {
        var id = await CreatePartnerAsync(
            new Dictionary<string, string> { ["storeId"] = "CR-114", ["apiKey"] = "sk-live-9" },
            secretProperties: ["apiKey"]);

        var row = await GetPartnerAsync(id);
        row.AdapterProperties["storeId"] = "CR-220";   // the only real edit
        await UpdatePartnerAsync(id, row);

        Assert.Equal("sk-live-9", await StoredPartnerPropertyAsync(id, "apiKey"));
        Assert.Equal("CR-220", await StoredPartnerPropertyAsync(id, "storeId"));
    }

    /// <summary>
    /// Retyping a locked value replaces it. A masked field that could not be written would leave
    /// a rotated credential unreachable except through the database.
    /// </summary>
    [Fact]
    public async Task A_new_value_typed_over_the_mask_replaces_the_stored_one()
    {
        var id = await CreatePartnerAsync(
            new Dictionary<string, string> { ["apiKey"] = "sk-live-9" },
            secretProperties: ["apiKey"]);

        var row = await GetPartnerAsync(id);
        row.AdapterProperties["apiKey"] = "sk-live-10";
        await UpdatePartnerAsync(id, row);

        Assert.Equal("sk-live-10", await StoredPartnerPropertyAsync(id, "apiKey"));
    }

    /// <summary>
    /// Locking is a display decision, not a one-way door: unlock, save, and the value reads back.
    /// Anyone who can unlock it could overwrite it anyway, so pretending otherwise would buy
    /// nothing and cost an operator their own data.
    /// </summary>
    [Fact]
    public async Task Unlocking_a_property_makes_it_readable_again()
    {
        var id = await CreatePartnerAsync(
            new Dictionary<string, string> { ["apiKey"] = "sk-live-9" },
            secretProperties: ["apiKey"]);

        var row = await GetPartnerAsync(id);
        row.SecretProperties = [];                    // the lock comes off; the value rides back masked
        await UpdatePartnerAsync(id, row);

        var after = await GetPartnerAsync(id);
        Assert.Equal("sk-live-9", after.AdapterProperties["apiKey"]);
        Assert.Empty(after.SecretProperties);
    }

    /// <summary>
    /// The feature is only worth having if the reference still resolves. Masking happens on the
    /// way out of the API; the pipeline reads the row, so a locked property fills an adapter field
    /// exactly as an open one does.
    /// </summary>
    [Fact]
    public async Task A_locked_property_still_fills_a_reference_at_run_time()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var partner = new Partner("Locked Token Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["apiKey"] = "sk-live-9" },
            SecretProperties = ["apiKey"]
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var doc = new Document(null, "Locked Token Doc", DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("Locked Token Sub", doc.Id, SubscriptionType.Internal, partner.Id);
        sub.Inactive = false;
        sub.SetDictionaries(
            new Dictionary<string, string> { ["ApiKey"] = "{{partner.apiKey}}" },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var xchange = await xchangeService.CreateXchange(sub, new XchangeFile("{\"id\":1}"));
        await db.SaveChangesAsync();

        Assert.Equal("sk-live-9", xchange.HandlerProperties["ApiKey"]);
    }

    // ------------------------------------------------------------- global values

    /// <summary>
    /// Same contract on the other bag. A value set is shared, so the flag is per value: locking
    /// the token must not take the base url down with it.
    /// </summary>
    [Fact]
    public async Task A_locked_global_value_is_masked_and_its_neighbours_are_not()
    {
        var id = await CreateGlobalSetAsync(
            new Dictionary<string, string> { ["baseUrl"] = "https://api.example.com", ["token"] = "glb-7" },
            secretProperties: ["token"]);

        var row = await GetGlobalSetAsync(id);

        Assert.Equal("https://api.example.com", row.Values["baseUrl"]);
        Assert.Equal(AdapterSecretProperties.Sentinel, row.Values["token"]);
    }

    /// <summary>
    /// The list endpoint feeds the reference picker, which is why it sends values at all — so it
    /// is also a way out for a locked one. The key stays, because a secret nobody can point at is
    /// no use; only the value goes.
    /// </summary>
    [Fact]
    public async Task The_list_endpoint_masks_a_locked_value_but_keeps_its_key()
    {
        var id = await CreateGlobalSetAsync(
            new Dictionary<string, string> { ["token"] = "glb-7" },
            secretProperties: ["token"]);

        var listed = await SearchGlobalSetsAsync();
        var row = listed.Single(r => r.Id == id);

        Assert.True(row.Values.ContainsKey("token"));
        Assert.Equal(AdapterSecretProperties.Sentinel, row.Values["token"]);
        Assert.DoesNotContain("glb-7", string.Join("|", row.Values.Values));
    }

    /// <summary>
    /// The same unrelated-edit round trip as the partner one. Global sets are edited far more
    /// often than the secret in them changes, so this is the path that would quietly destroy it.
    /// </summary>
    [Fact]
    public async Task Saving_a_masked_global_value_back_keeps_the_stored_value()
    {
        var id = await CreateGlobalSetAsync(
            new Dictionary<string, string> { ["baseUrl"] = "https://api.example.com", ["token"] = "glb-7" },
            secretProperties: ["token"]);

        var row = await GetGlobalSetAsync(id);
        row.Values["baseUrl"] = "https://api2.example.com";
        await UpdateGlobalSetAsync(id, row);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<GlobalAdapterValuesSet>().AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal("glb-7", stored.Values["token"]);
        Assert.Equal("https://api2.example.com", stored.Values["baseUrl"]);
    }

    // ------------------------------------------------------------------- helpers

    private async Task<int> CreatePartnerAsync(
        Dictionary<string, string> properties, List<string> secretProperties)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Create>(scope.ServiceProvider);

        return (int)await handler.Handle(new PartnerCreate
        {
            Name = $"secret-partner-{System.Guid.NewGuid():N}",
            AdapterProperties = properties,
            SecretProperties = secretProperties
        });
    }

    private async Task<PartnerUpdate> GetPartnerAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Get>(scope.ServiceProvider);
        return (PartnerUpdate)await handler.Handle(id);
    }

    private async Task UpdatePartnerAsync(int id, PartnerUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Partners.Update>(scope.ServiceProvider);
        await handler.Handle(id, model);
    }

    /// <summary>Reads past the API, because what the API returns is the thing under test.</summary>
    private async Task<string> StoredPartnerPropertyAsync(int id, string key)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == id);
        return partner.AdapterProperties[key];
    }

    private async Task<string> CreateGlobalSetAsync(
        Dictionary<string, string> values, List<string> secretProperties)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Create>(scope.ServiceProvider);
        var id = $"set{System.Guid.NewGuid():N}";

        await handler.Handle(new GlobalAdapterValuesSetCreate
        {
            Id = id,
            Name = id,
            Values = values,
            SecretProperties = secretProperties
        });
        return id;
    }

    private async Task<GlobalAdapterValuesSetRow> GetGlobalSetAsync(string id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Get>(scope.ServiceProvider);
        return (GlobalAdapterValuesSetRow)await handler.Handle(id);
    }

    private async Task UpdateGlobalSetAsync(string id, GlobalAdapterValuesSetRow model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Update>(scope.ServiceProvider);
        await handler.Handle(id, model);
    }

    private async Task<List<GlobalAdapterValuesSetRow>> SearchGlobalSetsAsync()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Search>(scope.ServiceProvider);
        var response = (SearchyResponse<GlobalAdapterValuesSetRow>)await handler.Handle(new SearchyRequest());
        return response.Result.ToList();
    }
}
