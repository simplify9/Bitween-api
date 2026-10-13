using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Passwords and stored secrets, over HTTP: an old password hash is replaced at the next sign-in,
/// guessing the old password on the change-password form locks out as sign-in does, and a bus gateway's
/// credentials come back masked and survive being saved back masked.
/// </summary>
[Collection("Http")]
public class PasswordAndSecretTests(HttpFixture fixture)
{
    /// <summary>PBKDF2-HMAC-SHA1 at 10,000 iterations, as accounts made before the move to SHA256 carry.</summary>
    const string V1Hash = "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ";
    const string V1Password = "Mtm@dmin!2";

    static string Email() => $"pw-{Guid.NewGuid():N}@example.com"[..40];

    async Task<string> NewMemberAsync(string email, string password)
    {
        using var admin = await fixture.AdminAsync();
        await Api.CreateAsync(admin, "/api/accounts", new { name = "Password Member", email, password, role = 2 });
        return email;
    }

    Task<string> StoredHashAsync(string email) =>
        fixture.InDbAsync(db => db.Set<Account>().Where(a => a.Email == email).Select(a => a.Password).SingleAsync());

    [Fact]
    public async Task An_old_hash_is_replaced_at_the_next_sign_in_and_the_password_still_works()
    {
        var email = await NewMemberAsync(Email(), "Before-Upgrade-2026!");
        await fixture.InDbAsync(db => db.Set<Account>().Where(a => a.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Password, V1Hash)));

        using (var client = fixture.Client())
            await HttpFixture.SignInAsync(client, email, V1Password);

        var upgraded = await StoredHashAsync(email);
        Assert.StartsWith("$SWHASH$V2$", upgraded);
        Assert.False(SecurePasswordHasher.NeedsRehash(upgraded));

        using var again = fixture.Client();
        await HttpFixture.SignInAsync(again, email, V1Password);
        Assert.Equal(upgraded, await StoredHashAsync(email));
    }

    [Fact]
    public async Task Guessing_the_old_password_locks_out_as_sign_in_does()
    {
        const string password = "Change-Me-Not-2026!";
        var email = await NewMemberAsync(Email(), password);
        using var member = fixture.Client();
        member.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await HttpFixture.SignInAsync(member, email, password));

        Task<HttpResponseMessage> Change(string old) => member.PostAsJsonAsync("/api/accounts/changePassword",
            new { OldPassword = old, NewPassword = "Changed-Pass-2026!" });

        for (var i = 0; i < SignInThrottle.PerAddressLimit; i++)
        {
            var wrong = await Change($"wrong-{i}");
            Assert.Contains("AUTHENTICATION_ERROR", await wrong.Content.ReadAsStringAsync());
        }

        // Locked now, even with the right one.
        var locked = await Change(password);
        Assert.False(locked.IsSuccessStatusCode);
        Assert.Contains("TOO_MANY_ATTEMPTS", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_bus_gateways_credentials_come_back_masked_and_survive_being_saved_back()
    {
        using var admin = await fixture.AdminAsync();
        var documentId = await Api.CreateAsync(admin, "/api/documents", new { name = Api.Unique("Bus doc"), documentFormat = "Json" });
        var name = Api.Unique("Bus gateway");
        var gatewayId = await Api.CreateAsync(admin, "/api/busgateways", new
        {
            name, documentId,
            endpointProperties = new { password = "hunter2-secret", region = "eu" },
        });

        var read = await Api.Json(await admin.GetAsync($"/api/busgateways/{gatewayId}"));
        var properties = read["endpointProperties"]!;
        Assert.Equal(Resources.DataSources.Secrets.Sentinel, (string)properties["password"]!);
        Assert.Equal("eu", (string)properties["region"]!);
        Assert.DoesNotContain("hunter2-secret", read.ToJsonString());

        // The form sends back what it was given: the stored password stays.
        await Api.Json(await admin.PostAsJsonAsync($"/api/busgateways/{gatewayId}", new
        {
            name, documentId,
            endpointProperties = new { password = Resources.DataSources.Secrets.Sentinel, region = "us" },
        }));
        var stored = await fixture.InDbAsync(db => db.Set<Domain.Gateway.BusGateway>()
            .AsNoTracking().SingleAsync(g => g.Id == gatewayId));
        Assert.Equal("hunter2-secret", stored.EndpointProperties["password"]);
        Assert.Equal("us", stored.EndpointProperties["region"]);
    }
}
