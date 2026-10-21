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

/// <summary>
/// Who uses a global value set. The subscription list withholds adapter properties, since they hold
/// credentials, so it says by name which value sets and partner properties each subscription
/// references - and deleting a set that something still references is refused.
/// </summary>
/// <remarks>
/// Before this the admin UI read the references out of the list's properties itself. Once those were
/// withheld it found none, and every value set read "Not referenced anywhere yet - safe to delete"
/// while dozens of subscriptions resolved their credentials from it.
/// </remarks>
[Collection("Bitween")]
public class GlobalValueReferenceTests(BitweenFixture fixture)
{
    [Fact]
    public async Task The_list_names_what_a_subscription_references_and_still_withholds_the_values()
    {
        var subscriptionId = await CreateSubscriptionAsync(
            handler: new() { ["Password"] = "{{globals.Spaces.secretKey}}", ["Url"] = "https://{{globals.spaces.host}}/up" },
            mapper: new() { ["Token"] = "{{GLOBALS.spaces.SECRETKEY}}" },
            receiver: new() { ["ApiKey"] = "{{partner.apiKey}}", ["Plain"] = "sk-live-9" });

        var row = await ListRowAsync(subscriptionId);

        var set = Assert.Single(row.References.Globals);
        Assert.Equal("Spaces", set.SetId, ignoreCase: true);
        // Two spellings of one key are one key, as the resolver reads them.
        Assert.Equal(new[] { "host", "secretKey" }, set.Keys.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "apiKey" }, row.References.PartnerProperties);

        // Names only: the values, the credential beside them included, still don't come back.
        Assert.Empty(row.HandlerProperties);
        Assert.Empty(row.MapperProperties);
        Assert.Empty(row.ReceiverProperties);
    }

    [Fact]
    public async Task A_subscription_that_references_nothing_lists_empty_references()
    {
        var subscriptionId = await CreateSubscriptionAsync(handler: new() { ["Url"] = "https://plain.example.test" });

        var row = await ListRowAsync(subscriptionId);

        Assert.Empty(row.References.Globals);
        Assert.Empty(row.References.PartnerProperties);
    }

    [Fact]
    public async Task A_set_a_subscription_references_is_not_deleted()
    {
        var setId = await CreateSetAsync();
        var name = $"Uses the set {Guid.NewGuid():N}";
        // A different case from the set's id, as the resolver still finds it.
        await CreateSubscriptionAsync(name, receiver: new() { ["Password"] = $"{{{{globals.{setId.ToUpperInvariant()}.password}}}}" });

        var refused = await Assert.ThrowsAsync<SWValidationException>(() => DeleteSetAsync(setId));

        Assert.Contains(name, refused.Message);
        Assert.True(await SetExistsAsync(setId));
    }

    [Fact]
    public async Task A_set_a_notification_channel_references_is_not_deleted()
    {
        var setId = await CreateSetAsync();
        var channel = new NotificationChannel
        {
            Name = $"Ops mail {Guid.NewGuid():N}",
            HandlerId = "NativeSmtpHandler",
            HandlerProperties = new Dictionary<string, string> { ["Password"] = $"{{{{globals.{setId}.password}}}}" },
        };
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(channel);
            await db.SaveChangesAsync();
        }

        var refused = await Assert.ThrowsAsync<SWValidationException>(() => DeleteSetAsync(setId));

        Assert.Contains(channel.Name, refused.Message);
        Assert.True(await SetExistsAsync(setId));
    }

    [Fact]
    public async Task A_set_nothing_references_is_deleted_even_when_another_set_is_in_use()
    {
        var unused = await CreateSetAsync();
        var used = await CreateSetAsync();
        await CreateSubscriptionAsync(handler: new() { ["Password"] = $"{{{{globals.{used}.password}}}}" });

        await DeleteSetAsync(unused);

        Assert.False(await SetExistsAsync(unused));
    }

    // ------------------------------------------------------------------- helpers

    private async Task<int> CreateSubscriptionAsync(string name = null,
        Dictionary<string, string> handler = null, Dictionary<string, string> mapper = null,
        Dictionary<string, string> receiver = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(null, $"Reference Doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription(name ?? $"Reference Sub {Guid.NewGuid():N}", doc.Id);
        sub.SetDictionaries(
            handler: handler ?? new Dictionary<string, string>(),
            mapper: mapper ?? new Dictionary<string, string>(),
            receiver: receiver ?? new Dictionary<string, string>(),
            document: new Dictionary<string, string>(),
            validator: new Dictionary<string, string>());
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    private async Task<SubscriptionSearch> ListRowAsync(int subscriptionId)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Search>(scope.ServiceProvider);
        var response = (SearchyResponse<SubscriptionSearch>)await handler.Handle(
            new SearchyRequest($"filter=Id:1:{subscriptionId}"));
        return Assert.Single(response.Result);
    }

    private async Task<string> CreateSetAsync()
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Create>(scope.ServiceProvider);
        var id = $"set{Guid.NewGuid():N}";
        await handler.Handle(new GlobalAdapterValuesSetCreate
        {
            Id = id,
            Name = id,
            Values = new Dictionary<string, string> { ["password"] = "s3cret" },
            SecretProperties = ["password"]
        });
        return id;
    }

    private async Task DeleteSetAsync(string id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.GlobalAdapterValuesSets.Delete>(scope.ServiceProvider);
        await handler.Handle(id, new DeleteGlobalAdapterValuesSetModel());
    }

    private async Task<bool> SetExistsAsync(string id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<GlobalAdapterValuesSet>().AsNoTracking().AnyAsync(s => s.Id == id);
    }
}
