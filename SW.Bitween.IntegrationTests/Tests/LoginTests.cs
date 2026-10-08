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
public class LoginTests(BitweenFixture fixture)
{
    private const string GoodPassword = "Correct-Horse-9!";

    /// <summary>
    /// One attempt in its own scope, because that is what a real request is. Sharing a scope across
    /// attempts would share a DbContext, and the lockout counter is written with a database-side
    /// UPDATE that the change tracker never sees — so a second attempt would read a stale account
    /// and the lockout would look broken when it isn't.
    /// </summary>
    private async Task<object> Login(string email, string password, string address = "203.0.113.1")
    {
        await using var scope = fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { Username = email, Password = password });
    }

    private async Task<Account> CreateAccount(string email, string password = GoodPassword,
        bool disabled = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        // A null password is the real state of an invited account and of a Microsoft-only
        // instance: the row exists purely to be matched by address, with nothing to verify against.
        var account = new Account("Login Test", email,
            password is null ? null : SecurePasswordHasher.Hash(password), AccountRole.Member);
        if (disabled) account.SetDisabled(true);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    /// <summary>Reads the account back through a fresh context, so it reflects what is committed.</summary>
    private async Task<Account> Reload(int accountId)
    {
        await using var scope = fixture.CreateScope();
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

    /// <summary>
    /// Only someone who knows the password learns the account is disabled; anyone else gets the
    /// answer an unknown address gets, so the message can't be used to list who has an account.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_on_a_disabled_account_gets_the_ordinary_refusal()
    {
        await CreateAccount("disabled-guess@test.local", disabled: true);

        var ex = await Assert.ThrowsAsync<SWException>(() => Login("disabled-guess@test.local", "wrong"));

        Assert.Equal("Invalid username or password.", ex.Message);
    }

    [Fact]
    public async Task An_account_that_has_no_password_cannot_be_signed_into()
    {
        await CreateAccount("nopassword@test.local", password: null);

        // The account an invitation creates, before the invitee has set anything. With nothing
        // stored to compare against, a verification that treats "no password" as "matches" hands
        // a token to anyone who can guess the address.
        await Assert.ThrowsAsync<SWException>(() => Login("nopassword@test.local", ""));
        await Assert.ThrowsAsync<SWException>(() => Login("nopassword@test.local", "anything"));
    }

    [Fact]
    public async Task An_empty_password_is_refused_for_an_account_that_has_one()
    {
        await CreateAccount("emptyattempt@test.local");

        // The other half: submitting nothing must be a failed attempt, not a skipped check.
        await Assert.ThrowsAsync<SWException>(() => Login("emptyattempt@test.local", ""));
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
    }

    /// <summary>
    /// Five wrong guesses used to lock the account for everyone, so anyone who knew an address
    /// could keep its owner out indefinitely. Now they lock out the address that made them.
    /// </summary>
    [Fact]
    public async Task Guessing_from_one_address_does_not_lock_out_the_owner_elsewhere()
    {
        await CreateAccount("targeted@test.local");

        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<SWException>(() => Login("targeted@test.local", "wrong", "198.51.100.7"));

        await Assert.ThrowsAsync<SWException>(() => Login("targeted@test.local", GoodPassword, "198.51.100.7"));
        Assert.NotNull(await Login("targeted@test.local", GoodPassword, "203.0.113.50"));
    }

    /// <summary>The account-wide backstop, for guessing spread over many addresses.</summary>
    [Fact]
    public async Task Guessing_spread_over_many_addresses_still_locks_the_account()
    {
        var account = await CreateAccount("spread@test.local");

        for (var attempt = 0; attempt < 20; attempt++)
            await Assert.ThrowsAsync<SWException>(() =>
                Login("spread@test.local", "wrong", $"198.51.100.{attempt % 4 + 10}"));

        var ex = await Assert.ThrowsAsync<SWException>(() => Login("spread@test.local", GoodPassword, "203.0.113.99"));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull((await Reload(account.Id)).LockoutEnd);
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

    private async Task<object> Refresh(string refreshToken)
    {
        await using var scope = fixture.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { RefreshToken = refreshToken });
    }

    private async Task<string> IssueRefreshToken(int accountId, TimeSpan age)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var token = new RefreshToken(accountId, LoginMethod.EmailAndPassword);
        db.Add(token);
        await db.SaveChangesAsync();
        var createdOn = DateTime.UtcNow - age;
        await db.Set<RefreshToken>().Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedOn, createdOn));
        return token.Id;
    }

    [Fact]
    public async Task A_recent_refresh_token_signs_in()
    {
        var account = await CreateAccount("refresh-fresh@test.local");
        var token = await IssueRefreshToken(account.Id, TimeSpan.FromDays(1));

        Assert.NotNull(await Refresh(token));
    }

    /// <summary>
    /// The cookie expires after 30 days; the stored token has to as well, or one copied out of a
    /// browser, or sent in the body by a legacy client, works forever.
    /// </summary>
    [Fact]
    public async Task A_refresh_token_idle_for_more_than_30_days_is_refused()
    {
        var account = await CreateAccount("refresh-stale@test.local");
        var token = await IssueRefreshToken(account.Id, TimeSpan.FromDays(31));

        await Assert.ThrowsAsync<SWException>(() => Refresh(token));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<RefreshToken>().AnyAsync(t => t.Id == token));
    }

    /// <summary>
    /// Disabling refused new sign-ins but not the token already issued, which kept every permission
    /// until it expired.
    /// </summary>
    [Fact]
    public async Task A_disabled_account_is_granted_nothing_on_a_token_it_already_holds()
    {
        var account = await CreateAccount("disabled-token@test.local");
        await using (var setup = fixture.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, Role.AdministratorId));
            await db.SaveChangesAsync();
            await db.Set<Account>().Where(a => a.Id == account.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Disabled, true));
        }

        await using var scope = fixture.CreateScope();
        var ctx = scope.As(account.Id);
        Assert.Empty(await ctx.GetPermissions(scope.ServiceProvider.GetRequiredService<BitweenDbContext>()));
    }
}
