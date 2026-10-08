using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Signing in with Microsoft through the whole login handler, against the real database. The
/// tokens are signed here, with the key the handler is told Microsoft publishes; everything after
/// that — matching the account, binding the Microsoft identity to it, refusing another identity or
/// a disabled account — is the code that runs in production.
/// </summary>
[Collection("Bitween")]
public class MicrosoftSignInTests(BitweenFixture fixture) : IDisposable
{
    const string ClientId = "11111111-1111-1111-1111-111111111111";
    const string Tenant = "22222222-2222-2222-2222-222222222222";
    static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test" };
    static int _seq;

    readonly Func<System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<SecurityKey>>> realKeys =
        SwapKeys();
    readonly (string ClientId, string TenantId) realSettings = Configure(fixture);

    static Func<Task<System.Collections.Generic.IEnumerable<SecurityKey>>> SwapKeys()
    {
        var real = AccountExtensions.MicrosoftSigningKeys;
        AccountExtensions.MicrosoftSigningKeys = () => Task.FromResult<System.Collections.Generic.IEnumerable<SecurityKey>>([Key]);
        return real;
    }

    static (string, string) Configure(BitweenFixture fixture)
    {
        using var scope = fixture.App.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<BitweenOptions>();
        var before = (options.MsalClientId, options.MsalTenantId);
        options.MsalClientId = ClientId;
        options.MsalTenantId = Tenant;
        return before;
    }

    public void Dispose()
    {
        AccountExtensions.MicrosoftSigningKeys = realKeys;
        using var scope = fixture.App.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<BitweenOptions>();
        (options.MsalClientId, options.MsalTenantId) = realSettings;
    }

    static string Token(string email, string oid, string tenant = Tenant) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: $"https://login.microsoftonline.com/{tenant}/v2.0",
            audience: ClientId,
            claims: [new Claim("preferred_username", email), new Claim("oid", oid), new Claim("tid", tenant)],
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)));

    async Task<object> SignIn(string msToken)
    {
        await using var scope = fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { MsToken = msToken });
    }

    async Task<Account> Member(bool disabled = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        // No password: a Microsoft-only member exists purely to be matched.
        var account = new Account("Microsoft Member", $"ms{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}@example.com"[..36],
            null, AccountRole.Member);
        if (disabled) account.SetDisabled(true);
        db.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    async Task<string> BoundIdentity(int accountId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Account>()
            .Where(a => a.Id == accountId).Select(a => a.MicrosoftIdentity).SingleAsync();
    }

    [Fact]
    public async Task The_first_sign_in_binds_the_Microsoft_identity_which_then_signs_in_whatever_its_address()
    {
        var account = await Member();
        var oid = Guid.NewGuid().ToString();

        Assert.NotNull(await SignIn(Token(account.Email, oid)));
        Assert.Equal($"{oid}@{Tenant}", await BoundIdentity(account.Id));

        // Renamed at Microsoft: the binding, not the address, says who this is.
        Assert.NotNull(await SignIn(Token("renamed@example.com", oid)));
    }

    [Fact]
    public async Task Another_Microsoft_identity_with_the_same_address_is_refused_once_one_is_bound()
    {
        var account = await Member();
        await SignIn(Token(account.Email, Guid.NewGuid().ToString()));

        var refused = await Assert.ThrowsAsync<SWException>(() => SignIn(Token(account.Email, Guid.NewGuid().ToString())));
        Assert.Contains("different Microsoft account", refused.Message);
    }

    [Fact]
    public async Task A_disabled_account_and_a_token_from_another_tenant_are_refused()
    {
        var disabled = await Member(disabled: true);
        Assert.Contains("disabled", (await Assert.ThrowsAsync<SWException>(() =>
            SignIn(Token(disabled.Email, Guid.NewGuid().ToString())))).Message);

        var account = await Member();
        await Assert.ThrowsAsync<SWException>(() =>
            SignIn(Token(account.Email, Guid.NewGuid().ToString(), tenant: "33333333-3333-3333-3333-333333333333")));
        Assert.Null(await BoundIdentity(account.Id));
    }
}
