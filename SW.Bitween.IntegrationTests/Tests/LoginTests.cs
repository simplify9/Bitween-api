using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The account login handler — the one door into the system, and the only place a password is
/// checked.
/// </summary>
/// <remarks>
/// Worth testing in full because most of the handler is refusals rather than the happy path, and a
/// refusal that silently stops refusing looks exactly like everything working. The lockout counter
/// especially: it is applied with a single database-side UPDATE so that concurrent wrong guesses
/// can't each read the same count and overwrite one another, which would let an attacker stay
/// permanently one attempt below the threshold.
/// </remarks>
[Collection("Bitween")]
public class LoginTests
{
    private readonly BitweenFixture _fixture;

    public LoginTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private const string GoodPassword = "Correct-Horse-9!";

    /// <summary>
    /// One attempt in its own scope, because that is what a real request is. Sharing a scope across
    /// attempts would share a DbContext, and the lockout counter is written with a database-side
    /// UPDATE that the change tracker never sees — so a second attempt would read a stale account
    /// and the lockout would look broken when it isn't.
    /// </summary>
    private async Task<object> Login(string email, string password)
    {
        await using var scope = _fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { Username = email, Password = password });
    }

    private async Task<Account> CreateAccount(string email, string password = GoodPassword,
        bool disabled = false)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = new Account("Login Test", email, SecurePasswordHasher.Hash(password), AccountRole.Member);
        if (disabled) account.SetDisabled(true);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    /// <summary>Reads the account back through a fresh context, so it reflects what is committed.</summary>
    private async Task<Account> Reload(int accountId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        return await db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == accountId);
    }

    [Fact]
    public async Task Correct_password_returns_a_token()
    {
        await CreateAccount("good@test.local");

        var result = await Login("good@test.local", GoodPassword);

        var jwt = result.GetType().GetProperty("Jwt")?.GetValue(result) as string;
        Assert.False(string.IsNullOrWhiteSpace(jwt));
    }

    [Fact]
    public async Task Email_matching_ignores_case()
    {
        await CreateAccount("mixedcase@test.local");

        // Nobody types their address the same way twice; a case-sensitive lookup would lock people
        // out of accounts that exist.
        var result = await Login("MixedCase@Test.Local", GoodPassword);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Wrong_password_is_refused()
    {
        await CreateAccount("wrongpass@test.local");

        await Assert.ThrowsAsync<SWException>(() => Login("wrongpass@test.local", "not-the-password"));
    }

    [Fact]
    public async Task An_unknown_email_is_refused_the_same_way_as_a_wrong_password()
    {
        var unknown = await Assert.ThrowsAsync<SWException>(() => Login("nobody-here@test.local", GoodPassword));

        await CreateAccount("exists@test.local");
        var wrongPassword = await Assert.ThrowsAsync<SWException>(() => Login("exists@test.local", "not-the-password"));

        // Identical wording on purpose: a different message would tell an attacker which addresses
        // are registered, turning the login form into an account directory.
        Assert.Equal(unknown.Message, wrongPassword.Message);
    }

    [Fact]
    public async Task A_disabled_account_cannot_sign_in()
    {
        await CreateAccount("disabled@test.local", disabled: true);

        // This is also what holds invitations shut: an invite creates the account up front, disabled
        // and password-less, and nothing else stands between the invitee and the system.
        var ex = await Assert.ThrowsAsync<SWException>(() => Login("disabled@test.local", GoodPassword));

        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_username_with_no_password_is_refused()
    {
        await CreateAccount("nopassword@test.local");

        // Without the explicit guard for this, an empty password skips verification entirely and
        // the caller is handed a token for any account whose address they can guess.
        await Assert.ThrowsAsync<SWException>(() => Login("nopassword@test.local", ""));
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var account = await CreateAccount("lockout@test.local");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<SWException>(() => Login("lockout@test.local", "wrong"));
        }

        // The point of the lockout: even the real password stops working while it holds.
        var ex = await Assert.ThrowsAsync<SWException>(() => Login("lockout@test.local", GoodPassword));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await Reload(account.Id);
        Assert.NotNull(stored.LockoutEnd);
    }

    [Fact]
    public async Task A_successful_login_clears_earlier_failures()
    {
        var account = await CreateAccount("resets@test.local");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsAsync<SWException>(() => Login("resets@test.local", "wrong"));
        }

        await Login("resets@test.local", GoodPassword);

        // Otherwise failures accumulate across weeks and the lockout eventually fires on a user who
        // has done nothing wrong.
        var stored = await Reload(account.Id);
        Assert.Equal(0, stored.FailedLoginCount);
    }
}
