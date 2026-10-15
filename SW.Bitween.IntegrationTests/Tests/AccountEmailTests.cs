using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Invites and password resets, emailed through the fixture's mail server (MailHog) and used the
/// way the person who gets them would: open the link, choose a password, sign in.
/// </summary>
[Collection("Bitween")]
public class AccountEmailTests(BitweenFixture fixture)
{
    const string StrongPassword = "Chosen-by-them-1";

    [Fact]
    public async Task An_invite_emails_a_link_that_sets_the_password_once()
    {
        await using var scope = fixture.CreateScope();
        var email = Address("invitee");
        var member = await AddInvitedMember(scope, email);

        var result = (InviteResultModel)await Handler<Invite>(scope).Handle(member, new InviteAccountModel());

        Assert.True(result.Emailed);
        Assert.Null(result.Link);
        var token = TokenIn(await MailTo(email, "You're invited to Bitween"));

        var info = (PasswordLinkInfoModel)await Handler<PasswordLink>(scope).Handle(new PasswordLinkModel { Token = token });
        Assert.Equal("Invite", info.Purpose);
        Assert.Equal(email, info.Email);

        await Handler<UsePasswordLink>(scope).Handle(new UsePasswordLinkModel { Token = token, Password = StrongPassword });

        var account = await Read(scope, member);
        Assert.True(SecurePasswordHasher.Verify(StrongPassword, account.Password));

        var again = await Assert.ThrowsAsync<SWValidationException>(() =>
            Handler<UsePasswordLink>(scope).Handle(new UsePasswordLinkModel { Token = token, Password = "Another-one-2" }));
        Assert.Contains(PasswordLinks.Invalid, again.Message);
    }

    [Fact]
    public async Task Without_a_mail_server_the_invite_link_is_handed_to_whoever_invited()
    {
        await using var scope = fixture.CreateScope();
        var member = await AddInvitedMember(scope, Address("no-mail"));
        var mailServer = scope.ServiceProvider.GetRequiredService<BitweenOptions>().MailServer;
        var host = mailServer.Host;
        mailServer.Host = null;
        try
        {
            var result = (InviteResultModel)await Handler<Invite>(scope).Handle(member, new InviteAccountModel());

            Assert.False(result.Emailed);
            Assert.StartsWith("https://bitween.test/set-password?token=", result.Link);
            var info = (PasswordLinkInfoModel)await Handler<PasswordLink>(scope)
                .Handle(new PasswordLinkModel { Token = TokenIn(result.Link) });
            Assert.Equal("Invite", info.Purpose);
        }
        finally
        {
            mailServer.Host = host;
        }
    }

    [Fact]
    public async Task A_resent_invite_makes_the_earlier_link_useless()
    {
        await using var scope = fixture.CreateScope();
        var email = Address("resent");
        var member = await AddInvitedMember(scope, email);

        await Handler<Invite>(scope).Handle(member, new InviteAccountModel());
        var first = TokenIn(await MailTo(email, "You're invited to Bitween"));
        await Handler<Invite>(scope).Handle(member, new InviteAccountModel());

        await Assert.ThrowsAsync<SWValidationException>(() =>
            Handler<PasswordLink>(scope).Handle(new PasswordLinkModel { Token = first }));
    }

    [Fact]
    public async Task A_member_who_outranks_the_caller_cannot_be_invited_by_them()
    {
        int member;
        await using (var adminScope = fixture.CreateScope())
            member = await AddInvitedMember(adminScope, Address("outranks"), Role.AdministratorId);

        // A scope of its own: a request works out its caller's permissions once.
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var helpdesk = new Role($"helpdesk-{Guid.NewGuid():N}"[..24], "Adds members",
            [Permissions.Users.View, Permissions.Users.Create]);
        db.Add(helpdesk);
        await db.SaveChangesAsync();
        var caller = await Account(scope, Address("helpdesk"), "irrelevant-hash", helpdesk.Id);
        scope.As(caller);

        await Assert.ThrowsAsync<SWValidationException>(() =>
            Handler<Invite>(scope).Handle(member, new InviteAccountModel()));
    }

    [Fact]
    public async Task A_reset_signs_the_member_out_everywhere_and_tells_them()
    {
        await using var scope = fixture.CreateScope();
        var email = Address("forgetful");
        var member = await Account(scope, email, SecurePasswordHasher.Hash("Forgotten-pass-1"));
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        db.Add(new RefreshToken(member, LoginMethod.EmailAndPassword));
        await db.SaveChangesAsync();

        // Typed as people type it: the address is matched whatever its case and spaces.
        await Handler<ForgotPassword>(scope).Handle(new ForgotPasswordModel { Email = $"  {email.ToUpperInvariant()} " });
        var token = TokenIn(await MailTo(email, "Reset your Bitween password"));

        await Handler<UsePasswordLink>(scope).Handle(new UsePasswordLinkModel { Token = token, Password = StrongPassword });

        Assert.True(SecurePasswordHasher.Verify(StrongPassword, (await Read(scope, member)).Password));
        Assert.False(await db.Set<RefreshToken>().AnyAsync(t => t.AccountId == member));
        await MailTo(email, "Your Bitween password was changed");
    }

    [Fact]
    public async Task A_reset_asked_twice_in_a_minute_is_emailed_once()
    {
        await using var scope = fixture.CreateScope();
        var email = Address("impatient");
        await Account(scope, email, SecurePasswordHasher.Hash("Forgotten-pass-1"));

        await Handler<ForgotPassword>(scope).Handle(new ForgotPasswordModel { Email = email });
        await MailTo(email, "Reset your Bitween password");
        await Handler<ForgotPassword>(scope).Handle(new ForgotPasswordModel { Email = email });
        await Task.Delay(1500);

        Assert.Single(await fixture.MailedAsync(email, "Reset your Bitween password"));
    }

    [Fact]
    public async Task A_reset_for_an_address_without_an_account_answers_the_same_and_sends_nothing()
    {
        await using var scope = fixture.CreateScope();
        var email = Address("nobody");

        var answer = await Handler<ForgotPassword>(scope).Handle(new ForgotPasswordModel { Email = email });
        await Task.Delay(1500);

        Assert.Null(answer);
        Assert.Empty(await fixture.MailedAsync(email, "Reset your Bitween password"));
    }

    [Fact]
    public async Task A_link_used_twice_at_once_sets_one_password()
    {
        int member;
        string token;
        await using (var scope = fixture.CreateScope())
        {
            member = await Account(scope, Address("twice"), SecurePasswordHasher.Hash("Forgotten-pass-1"));
            (var link, token) = AccountLink.Issue(member, AccountLinkPurpose.PasswordReset, DateTime.UtcNow);
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(link);
            await db.SaveChangesAsync();
        }

        // Each on a request of its own, as two clicks of the same button would be.
        async Task<bool> Use(string password)
        {
            await using var scope = fixture.CreateScope();
            try
            {
                await Handler<UsePasswordLink>(scope).Handle(new UsePasswordLinkModel { Token = token, Password = password });
                return true;
            }
            catch (SWValidationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Use("First-choice-1"), Use("Second-choice-2"));

        Assert.Single(results, worked => worked);
        await using var check = fixture.CreateScope();
        var stored = (await Read(check, member)).Password;
        Assert.True(SecurePasswordHasher.Verify(results[0] ? "First-choice-1" : "Second-choice-2", stored));
    }

    [Fact]
    public async Task An_expired_link_is_refused()
    {
        await using var scope = fixture.CreateScope();
        var member = await Account(scope, Address("late"), SecurePasswordHasher.Hash("Forgotten-pass-1"));
        var (link, token) = AccountLink.Issue(member, AccountLinkPurpose.PasswordReset, DateTime.UtcNow.AddHours(-2));
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        db.Add(link);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SWValidationException>(() =>
            Handler<UsePasswordLink>(scope).Handle(new UsePasswordLinkModel { Token = token, Password = StrongPassword }));
    }

    [Fact]
    public async Task A_member_with_a_password_is_not_sent_an_invite()
    {
        await using var scope = fixture.CreateScope();
        var member = await Account(scope, Address("has-one"), SecurePasswordHasher.Hash("Already-set-1"));
        scope.Superuser();

        var refused = await Assert.ThrowsAsync<SWValidationException>(() =>
            Handler<Invite>(scope).Handle(member, new InviteAccountModel()));
        Assert.Contains("already has a password", refused.Message);
    }

    /// <summary>An administrator adds a member to be invited, and stays signed in as that administrator.</summary>
    async Task<int> AddInvitedMember(AsyncServiceScope scope, string email, params int[] roleIds)
    {
        var admin = await Account(scope, Address("admin"), "irrelevant-hash", Role.AdministratorId);
        scope.As(admin);
        return (int)await Handler<Create>(scope).Handle(new CreateAccountModel
        {
            Name = "Invited Person",
            Email = email,
            Invite = true,
            RoleIds = roleIds.ToList()
        });
    }

    static async Task<int> Account(AsyncServiceScope scope, string email, string passwordHash, params int[] roleIds)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var account = new Account("Test Person", email, passwordHash, AccountRole.Member);
        db.Add(account);
        await db.SaveChangesAsync();
        foreach (var roleId in roleIds) db.Add(new AccountRoleLink(account.Id, roleId));
        await db.SaveChangesAsync();
        return account.Id;
    }

    static async Task<Account> Read(AsyncServiceScope scope, int id) =>
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Account>()
            .AsNoTracking().SingleAsync(a => a.Id == id);

    static T Handler<T>(AsyncServiceScope scope) => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider);

    /// <summary>The one email of this subject to the address, waited for: some are sent in the background.</summary>
    async Task<MimeKit.MimeMessage> MailTo(string email, string subject)
    {
        for (var i = 0; i < 50; i++)
        {
            var found = await fixture.MailedAsync(email, subject);
            if (found.Count > 0) return Assert.Single(found);
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException($"No \"{subject}\" email reached {email}.");
    }

    static string TokenIn(MimeKit.MimeMessage message) => TokenIn(message.TextBody);

    static string TokenIn(string text) => Regex.Match(text, "token=([A-Za-z0-9_-]+)").Groups[1].Value;

    static string Address(string label) => $"{label}-{Guid.NewGuid():N}"[..(label.Length + 13)] + "@test.local";
}
