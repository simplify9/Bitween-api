using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// With an encryption key configured, credential columns hold ciphertext in the database and
/// plain values in the application; without one, nothing changes.
/// </summary>
[Collection("Bitween")]
public class SecretColumnEncryptionTests(BitweenFixture fixture)
{
    private const string Secret = "partner-password-in-clear";

    [Fact]
    public async Task A_partner_credential_is_stored_encrypted_and_read_back_plain()
    {
        SecretColumnCipher.Configure("integration test passphrase");
        try
        {
            var partnerId = await CreatePartnerAsync();

            Assert.DoesNotContain(Secret, await RawAdapterPropertiesAsync(partnerId));
            Assert.Equal(Secret, (await ReadPartnerAsync(partnerId)).AdapterProperties["password"]);
        }
        finally
        {
            SecretColumnCipher.Configure(null);
        }
    }

    [Fact]
    public async Task A_credential_saved_before_encryption_is_encrypted_by_the_pass()
    {
        var partnerId = await CreatePartnerAsync(); // no key yet: stored in clear
        Assert.Contains(Secret, await RawAdapterPropertiesAsync(partnerId));

        SecretColumnCipher.Configure("integration test passphrase");
        try
        {
            var pass = ActivatorUtilities.CreateInstance<SecretColumnEncryptionPass>(fixture.App.Services);
            await pass.RunAsync(CancellationToken.None);

            Assert.DoesNotContain(Secret, await RawAdapterPropertiesAsync(partnerId));
            Assert.Equal(Secret, (await ReadPartnerAsync(partnerId)).AdapterProperties["password"]);
        }
        finally
        {
            // Everything the pass encrypted would be unreadable to the rest of the collection, so it
            // is written back in clear: with no key and the old one as previous, values are read
            // with the old key and written unencrypted.
            var decrypt = ActivatorUtilities.CreateInstance<SecretColumnEncryptionPass>(fixture.App.Services);
            SecretColumnCipher.Configure(null, previousPassphrase: "integration test passphrase");
            await decrypt.RunAsync(CancellationToken.None);
            SecretColumnCipher.Configure(null);
        }
    }

    private async Task<int> CreatePartnerAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner($"Encrypted {Guid.NewGuid():N}")
        {
            AdapterProperties = new Dictionary<string, string> { ["password"] = Secret },
        };
        db.Add(partner);
        await db.SaveChangesAsync();
        return partner.Id;
    }

    private async Task<Partner> ReadPartnerAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<string> RawAdapterPropertiesAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Database
            .SqlQueryRaw<string>("SELECT adapter_properties::text AS \"Value\" FROM infolink.partner WHERE id = {0}", id)
            .SingleAsync();
    }
}
