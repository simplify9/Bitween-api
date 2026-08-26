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
/// What happens to a saved password when the person editing an integration never sees it.
/// </summary>
/// <remarks>
/// Secrets are masked on the way out as <c>__private__</c> and restored on the way back in. That
/// leaves an obvious way to destroy one by accident: the browser sends back the dots it was shown,
/// and a handler that took them literally would overwrite a working password with the placeholder
/// — breaking the integration at the next run, with nothing in the audit trail that looks like a
/// password change. These tests hold that behaviour in place from both directions: the sentinel
/// never lands in storage, and a real new value still does.
/// </remarks>
[Collection("Bitween")]
public class SubscriptionSecretTests
{
    private const string Sentinel = "__private__";
    private const string RealPassword = "s3cr3t-smtp-password";

    private readonly BitweenFixture _fixture;

    public SubscriptionSecretTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    private async Task<(int subscriptionId, int documentId, int partnerId)> AnIntegrationWithASecret()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var document = new Document(null, Unique("Secret doc"), DocumentFormat.Json);
        db.Set<Document>().Add(document);
        var partner = new Partner(Unique("Secret partner"));
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        scope.Superuser();
        var create = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider);
        var id = (int)await create.Handle(new SubscriptionCreate
        {
            Name = Unique("Sends mail"),
            DocumentId = document.Id,
            PartnerId = partner.Id,
            Type = SubscriptionType.ApiCall,
            HandlerProperties =
            [
                new KeyAndValue { Key = "Host", Value = "smtp.example.com" },
                new KeyAndValue { Key = "Password", Value = RealPassword },
            ],
        });

        return (id, document.Id, partner.Id);
    }

    private async Task Update(int id, int documentId, int partnerId, params KeyAndValue[] handlerProperties)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var update = ActivatorUtilities.CreateInstance<Resources.Subscriptions.Update>(scope.ServiceProvider);
        await update.Handle(id, new SubscriptionUpdate
        {
            Name = Unique("Sends mail"),
            DocumentId = documentId,
            PartnerId = partnerId,
            HandlerProperties = handlerProperties,
        });
    }

    private async Task<IReadOnlyDictionary<string, string>> StoredProperties(int id)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == id);
        return stored.HandlerProperties;
    }

    [Fact]
    public async Task Saving_the_mask_back_keeps_the_stored_secret()
    {
        var (id, documentId, partnerId) = await AnIntegrationWithASecret();

        // Exactly what the browser posts when someone edits the host and leaves the password alone.
        await Update(id, documentId, partnerId,
            new KeyAndValue { Key = "Host", Value = "smtp.newhost.com" },
            new KeyAndValue { Key = "Password", Value = Sentinel });

        var properties = await StoredProperties(id);

        Assert.Equal(RealPassword, properties["Password"]);
        Assert.Equal("smtp.newhost.com", properties["Host"]);
    }

    [Fact]
    public async Task The_mask_is_never_what_gets_stored()
    {
        var (id, documentId, partnerId) = await AnIntegrationWithASecret();

        await Update(id, documentId, partnerId,
            new KeyAndValue { Key = "Host", Value = "smtp.example.com" },
            new KeyAndValue { Key = "Password", Value = Sentinel });

        // Stated separately from the assertion above because this is the failure that would be
        // silent: the integration keeps saving fine and only breaks when it next tries to connect.
        Assert.DoesNotContain(Sentinel, (await StoredProperties(id)).Values);
    }

    [Fact]
    public async Task A_real_new_secret_replaces_the_old_one()
    {
        var (id, documentId, partnerId) = await AnIntegrationWithASecret();

        await Update(id, documentId, partnerId,
            new KeyAndValue { Key = "Host", Value = "smtp.example.com" },
            new KeyAndValue { Key = "Password", Value = "a-different-password" });

        // The protection must not become a trap where the password can never be changed.
        Assert.Equal("a-different-password", (await StoredProperties(id))["Password"]);
    }

    [Fact]
    public async Task Dropping_a_property_removes_it_rather_than_restoring_it()
    {
        var (id, documentId, partnerId) = await AnIntegrationWithASecret();

        await Update(id, documentId, partnerId,
            new KeyAndValue { Key = "Host", Value = "smtp.example.com" });

        // Only a mask restores the stored value. A property the caller omits entirely is gone —
        // otherwise a setting could never be cleared once set.
        Assert.False((await StoredProperties(id)).ContainsKey("Password"));
    }

    [Fact]
    public async Task A_mask_for_a_property_that_was_never_stored_is_dropped()
    {
        var (id, documentId, partnerId) = await AnIntegrationWithASecret();

        await Update(id, documentId, partnerId,
            new KeyAndValue { Key = "Host", Value = "smtp.example.com" },
            new KeyAndValue { Key = "Password", Value = Sentinel },
            new KeyAndValue { Key = "ApiKey", Value = Sentinel });

        // There is nothing to restore, so the sentinel must not be written through as if it were
        // the value — that is how the literal string "__private__" ends up being sent as an API key.
        var properties = await StoredProperties(id);
        Assert.False(properties.ContainsKey("ApiKey"));
        Assert.Equal(RealPassword, properties["Password"]);
    }
}
