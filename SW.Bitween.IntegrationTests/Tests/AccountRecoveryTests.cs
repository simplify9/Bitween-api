using System;
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
/// Getting a user back in when they can't sign in themselves.
/// </summary>
/// <remarks>
/// Bitween sends no mail, so there is no self-service reset link — an administrator acting on
/// someone's behalf is the only route back in, which makes it worth proving it actually works
/// end to end rather than assuming. These tests also pin down that resetting a password and
/// clearing a lockout are two separate actions, because that is genuinely surprising: an admin who
/// resets a locked-out user's password and stops there has not let them back in.
/// </remarks>
[Collection("Bitween")]
public class AccountRecoveryTests
{
    private readonly BitweenFixture _fixture;

    public AccountRecoveryTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private const string OldPassword = "Old-Password-1!";
    private const string NewPassword = "Brand-New-Password-2!";

    private async Task<Account> CreateAccount(string email, params int[] roleIds)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = new Account("Recovery Test", email, SecurePasswordHasher.Hash(OldPassword),
            AccountRole.Member);
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync();

        foreach (var roleId in roleIds)
            db.Set<AccountRoleLink>().Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();

        return account;
    }

    private async Task<object> Login(string email, string password)
    {
        await using var scope = _fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Login>(scope.ServiceProvider);
        return await handler.Handle(new UserLogin { Username = email, Password = password });
    }

    [Fact]
    public async Task An_administrator_can_set_someone_elses_password()
    {
        var target = await CreateAccount("reset-target@test.local");
        var admin = await CreateAccount("reset-admin@test.local", Role.AdministratorId);

        await using (var scope = _fixture.CreateScope())
        {
            scope.As(admin.Id);
            var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.SetPassword>(scope.ServiceProvider);
            await handler.Handle(target.Id, new SetAccountPasswordModel { Password = NewPassword });
        }

        // The reset is only real if the new password actually opens the door.
        Assert.NotNull(await Login("reset-target@test.local", NewPassword));
        await Assert.ThrowsAsync<SWException>(() => Login("reset-target@test.local", OldPassword));
    }

    [Fact]
    public async Task A_member_cannot_set_another_persons_password()
    {
        var target = await CreateAccount("victim@test.local");
        var member = await CreateAccount("nosy-member@test.local", Role.MemberId);

        await using var scope = _fixture.CreateScope();
        scope.As(member.Id);
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.SetPassword>(scope.ServiceProvider);

        // Users.Edit belongs to administrators; without this a Member could take over any account.
        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            handler.Handle(target.Id, new SetAccountPasswordModel { Password = NewPassword }));
    }

    [Fact]
    public async Task Setting_your_own_password_is_refused()
    {
        var admin = await CreateAccount("self-reset@test.local", Role.AdministratorId);

        await using var scope = _fixture.CreateScope();
        scope.As(admin.Id);
        var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.SetPassword>(scope.ServiceProvider);

        // Changing your own goes through ChangePassword, which demands the current one — otherwise
        // an unattended signed-in session is enough to take the account over permanently.
        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            handler.Handle(admin.Id, new SetAccountPasswordModel { Password = NewPassword }));
        Assert.StartsWith("USE_CHANGE_PASSWORD", ex.Message);
    }

    [Fact]
    public async Task Resetting_a_locked_out_users_password_does_not_by_itself_let_them_back_in()
    {
        var target = await CreateAccount("locked-out@test.local");
        var admin = await CreateAccount("unlock-admin@test.local", Role.AdministratorId);

        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<SWException>(() => Login("locked-out@test.local", "wrong"));

        await using (var scope = _fixture.CreateScope())
        {
            scope.As(admin.Id);
            var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.SetPassword>(scope.ServiceProvider);
            await handler.Handle(target.Id, new SetAccountPasswordModel { Password = NewPassword });
        }

        // Still locked: SetPassword rehashes the password and nothing else. An administrator who
        // stops here has done half the job and the user is still shut out.
        var ex = await Assert.ThrowsAsync<SWException>(() => Login("locked-out@test.local", NewPassword));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using (var scope = _fixture.CreateScope())
        {
            scope.As(admin.Id);
            var handler = ActivatorUtilities.CreateInstance<Resources.Accounts.Unlock>(scope.ServiceProvider);
            await handler.Handle(target.Id, new UnlockAccountModel());
        }

        Assert.NotNull(await Login("locked-out@test.local", NewPassword));
    }
}
