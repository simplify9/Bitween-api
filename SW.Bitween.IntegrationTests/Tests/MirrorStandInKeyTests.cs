using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services.Mirror;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The key a mirror's test calls a gateway as a partner with: the stand-in a copy gives the partner's
/// key. A stand-in made before staging's Token:Key changed is set to the current one; a key someone set
/// on staging is never touched.
/// </summary>
[Collection("Bitween")]
public class MirrorStandInKeyTests(BitweenFixture fixture)
{
    static readonly MirrorFaker Faker = new(Encoding.UTF8.GetBytes("staging's Token:Key today"));
    static readonly MirrorFaker Before = new(Encoding.UTF8.GetBytes("staging's Token:Key at the copy"));

    static string StandIn(MirrorFaker faker, string partner, string key) => faker.Secret($"partners/{partner}/key/{key}");

    [Fact]
    public async Task A_stand_in_from_before_the_key_changed_is_set_again_and_keys_set_on_staging_stay()
    {
        var name = $"Acme {Guid.NewGuid():N}";
        var own = Guid.NewGuid().ToString("N");
        await CreatePartnerAsync(name, ("main", StandIn(Before, name, "main")), ("ops", own));

        var key = await StandInKeyAsync(name);

        Assert.Equal(StandIn(Faker, name, "main"), key);
        var keys = await KeysAsync(name);
        Assert.Equal(PartnerKeyHash.Of(key), keys.Single(k => k.Name == "main").Key);
        Assert.Equal(PartnerKeyHash.Of(own), keys.Single(k => k.Name == "ops").Key);
    }

    [Fact]
    public async Task A_current_stand_in_is_used_as_it_is()
    {
        var name = $"Acme {Guid.NewGuid():N}";
        await CreatePartnerAsync(name, ("main", StandIn(Faker, name, "main")));

        Assert.Equal(StandIn(Faker, name, "main"), await StandInKeyAsync(name));
    }

    [Fact]
    public async Task A_partner_with_only_keys_set_on_staging_has_none_to_call_with()
    {
        var name = $"Acme {Guid.NewGuid():N}";
        var own = Guid.NewGuid().ToString("N");
        await CreatePartnerAsync(name, ("main", own));

        Assert.Null(await StandInKeyAsync(name));
        Assert.Equal(PartnerKeyHash.Of(own), (await KeysAsync(name)).Single().Key);
    }

    async Task<string> StandInKeyAsync(string name)
    {
        await using var scope = fixture.CreateScope();
        return await MirrorService.StandInKeyAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>(), name, Faker, CancellationToken.None);
    }

    async Task CreatePartnerAsync(string name, params (string Name, string Key)[] keys)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(name);
        partner.SetApiCredentials(keys.Select(k => new ApiCredential(k.Name, k.Key)));
        db.Add(partner);
        await db.SaveChangesAsync();
    }

    async Task<ApiCredential[]> KeysAsync(string name)
    {
        await using var scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Partner>().AsNoTracking()
            .SingleAsync(p => p.Name == name)).ApiCredentials.ToArray();
    }
}
