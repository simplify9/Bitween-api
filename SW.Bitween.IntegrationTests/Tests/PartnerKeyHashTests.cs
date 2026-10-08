using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Partner API keys are stored as hashes, so a copy of the database is not every partner's key.
/// Keys stored before this are converted at startup, and still work until they are.
/// </summary>
[Collection("Bitween")]
public class PartnerKeyHashTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_key_is_stored_as_its_hash_and_still_authorises()
    {
        var key = Guid.NewGuid().ToString("N");
        var partnerId = await CreatePartnerAsync(key);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = (await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == partnerId)).ApiCredentials.Single();

        Assert.Equal(PartnerKeyHash.Of(key), stored.Key);
        Assert.Equal(key[..5], stored.KeyPrefix);
        Assert.True(await AuthorisesAsync(key));
    }

    [Fact]
    public async Task A_key_stored_before_hashing_works_and_is_converted_at_startup()
    {
        var key = Guid.NewGuid().ToString("N");
        var partnerId = await CreatePartnerAsync(key);

        // The state an upgraded database starts in: the key itself in the column.
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE infolink.partner_api_credential SET key = {0}, key_prefix = NULL WHERE partner_id = {1}",
                key, partnerId);
        }

        Assert.True(await AuthorisesAsync(key));

        await using (var scope = fixture.CreateScope())
            await PartnerKeyHashing.HashStoredKeys(
                scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), NullLogger.Instance);

        await using var check = fixture.CreateScope();
        var stored = (await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == partnerId)).ApiCredentials.Single();
        Assert.Equal(PartnerKeyHash.Of(key), stored.Key);
        Assert.True(await AuthorisesAsync(key));
    }

    [Fact]
    public async Task The_hash_itself_is_not_a_key()
    {
        var key = Guid.NewGuid().ToString("N");
        await CreatePartnerAsync(key);

        // Someone holding a copy of the table has the hash. Presenting it must get them nowhere.
        Assert.False(await AuthorisesAsync(PartnerKeyHash.Of(key)));
    }

    private async Task<int> CreatePartnerAsync(string key)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner($"Hashed {Guid.NewGuid():N}");
        partner.SetApiCredentials([new ApiCredential("main", key)]);
        db.Add(partner);
        await db.SaveChangesAsync();
        return partner.Id;
    }

    /// <summary>
    /// Asks the partner-facing exchange lookup, which authorises the key before anything else: a
    /// refused key is an unauthorised error, an accepted one goes on to look for an exchange.
    /// </summary>
    private async Task<bool> AuthorisesAsync(string key)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
            new ClaimsPrincipal(new ClaimsIdentity()),
            [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);
        var get = ActivatorUtilities.CreateInstance<Resources.Xchanges.Get>(scope.ServiceProvider);
        try
        {
            await get.Handle("no-such-exchange");
            return true;
        }
        catch (SWUnauthorizedException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
