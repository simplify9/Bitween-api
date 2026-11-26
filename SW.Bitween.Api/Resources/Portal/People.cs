using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// What a partner's admin may do to their company's people, and to whom. The same rules as our
/// team's members pages, narrowed to one company: only partner roles, only within what the admin
/// holds, never one that manages people, and never to themselves or another of their admins.
/// </summary>
internal static class PortalPeople
{
    private const string AdminKey = Model.Permissions.Portal.PeopleEdit;

    /// <summary>
    /// The roles this admin may give, built in first and the least access first, so a new person
    /// starts on the least unless the admin picks more.
    /// </summary>
    public static async Task<List<Role>> GrantableAsync(BitweenDbContext db, PortalCaller caller)
    {
        var roles = await db.Set<Role>().AsNoTracking()
            .Where(r => r.ForPartners && (r.PartnerId == null || r.PartnerId == caller.PartnerId)).ToListAsync();
        return roles
            .Select(r => (Role: r, Grants: r.GetEffectivePermissions() ?? []))
            .Where(r => !r.Grants.Contains(AdminKey) && r.Grants.All(caller.Granted.Contains))
            .OrderBy(r => r.Role.IsSystem ? 0 : 1).ThenBy(r => r.Grants.Count).ThenBy(r => r.Role.Name)
            .Select(r => r.Role)
            .ToList();
    }

    public static async Task EnsureGrantable(BitweenDbContext db, PortalCaller caller, int roleId)
    {
        if ((await GrantableAsync(db, caller)).All(r => r.Id != roleId))
            throw new SWValidationException("ROLE_NOT_ALLOWED",
                "You can't give that role. Ask us if someone at your company needs more.");
    }

    /// <summary>One of the caller's company's people they may change, or a refusal saying why not.</summary>
    public static async Task<Account> ChangeableAsync(BitweenDbContext db, RequestContext ctx, PortalCaller caller,
        int accountId)
    {
        var account = await db.Set<Account>().FindAsync(accountId);
        // Someone at another company, or one of our team, is simply not there for them.
        if (account is null || account.IsService || account.PartnerId != caller.PartnerId)
            throw new SWNotFoundException(accountId.ToString());
        if (accountId == Convert.ToInt32(ctx.GetNameIdentifier()))
            throw new SWValidationException("NOT_YOURSELF", "You can't change your own account here.");

        var theirs = await RequestContextExtensions.GetPermissionsOf(db, accountId);
        if (theirs.Contains(AdminKey))
            throw new SWValidationException("ANOTHER_ADMIN",
                "Only we can change another admin of your company. Ask us if this needs to change.");
        if (!theirs.IsSubsetOf(caller.Granted))
            throw new SWValidationException("ACCOUNT_OUTRANKS_YOU",
                "This person can do things you can't, so you can't change their account.");
        return account;
    }

    public static async Task<string> NameOfAsync(BitweenDbContext db, RequestContext ctx)
    {
        var id = Convert.ToInt32(ctx.GetNameIdentifier());
        return await db.Set<Account>().Where(a => a.Id == id).Select(a => a.DisplayName).FirstOrDefaultAsync()
               ?? "Your company's admin";
    }
}

/// <summary>Their company's people, with the role each holds and whether they're still to sign in.</summary>
[HandlerName("people")]
public class People(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        // Managing people needs the list too, so a role that may change them may see them.
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext,
            Model.Permissions.Portal.PeopleView, Model.Permissions.Portal.PeopleEdit);
        var me = Convert.ToInt32(requestContext.GetNameIdentifier());
        var accounts = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => a.PartnerId == caller.PartnerId && a.Kind == AccountKind.Person)
            .OrderBy(a => a.DisplayName)
            .Select(a => new
            {
                a.Id, a.DisplayName, a.Email, a.Disabled, a.LastSignInOn, a.CreatedOn,
                HasPassword = a.Password != null && a.Password != ""
            })
            .ToListAsync();

        var ids = accounts.Select(a => a.Id).ToList();
        var roles = await AccountRoles.For(dbContext, ids);
        var invites = await dbContext.Set<AccountLink>().AsNoTracking()
            .Where(l => ids.Contains(l.AccountId) && l.Purpose == AccountLinkPurpose.Invite)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, ExpiresOn = g.Max(l => l.ExpiresOn) })
            .ToDictionaryAsync(i => i.AccountId, i => i.ExpiresOn);
        // Changeable as ChangeableAsync decides: not another admin, and nothing they hold beyond the caller.
        var changeable = new HashSet<int>();
        foreach (var id in ids)
        {
            var theirs = await RequestContextExtensions.GetPermissionsOf(dbContext, id);
            if (!theirs.Contains(Model.Permissions.Portal.PeopleEdit) && theirs.IsSubsetOf(caller.Granted))
                changeable.Add(id);
        }

        var canEdit = caller.Can(Model.Permissions.Portal.PeopleEdit);
        return accounts.Select(a =>
        {
            var held = roles.GetValueOrDefault(a.Id, []);
            return new PortalPerson
            {
                Id = a.Id,
                Name = a.DisplayName,
                Email = a.Email,
                Role = held.Count == 1 ? held[0] : null,
                Disabled = a.Disabled,
                Invited = !a.HasPassword && invites.ContainsKey(a.Id),
                HasPassword = a.HasPassword,
                CreatedOn = a.CreatedOn,
                InviteExpiresOn = !a.HasPassword && invites.TryGetValue(a.Id, out var on) ? on : null,
                LastSignInOn = a.LastSignInOn,
                IsYou = a.Id == me,
                CanChange = canEdit && a.Id != me && changeable.Contains(a.Id)
            };
        }).ToList();
    }
}

/// <summary>The roles their admin may give.</summary>
[HandlerName("peopleroles")]
public class PeopleRoles(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        return (await PortalPeople.GrantableAsync(dbContext, caller))
            .Select(r => new PortalRoleChoice { Id = r.Id, Name = r.Name, Description = r.Description, BuiltIn = r.IsSystem })
            .ToList();
    }
}

/// <summary>Adds someone at their company and invites them to choose a password.</summary>
[HandlerName("addperson")]
public class AddPerson(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, AccountMail mail)
    : ICommandHandler<PortalAddPerson, object>
{
    public async Task<object> Handle(PortalAddPerson request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        await PortalPeople.EnsureGrantable(dbContext, caller, request.RoleId);

        // Checked first, so a refusal leaves nobody half added.
        if (options.DisableEmailPasswordLogin && !mail.CanSend)
            throw new SWValidationException("NO_MAIL_SERVER",
                "People can't be invited until a mail server is set up. Ask us to add them.");

        var email = request.Email.Trim();
        // Said the same whoever has it, so it doesn't tell them which addresses sign in elsewhere.
        if (await dbContext.Set<Account>().AnyAsync(a => a.Email == email))
            throw new SWValidationException("EMAIL_TAKEN", "That email can't be used. Ask us if it should be.");

        var account = new Account(request.Name.Trim(), email, null, AccountRoles.LegacyRoleFor([request.RoleId]), caller.PartnerId);
        dbContext.Add(account);
        await dbContext.SaveChangesAsync();
        await AccountRoles.Set(dbContext, account.Id, [request.RoleId]);
        await dbContext.SaveChangesAsync();

        var invite = await Invite.SendAsync(dbContext, options, mail, account, await PortalPeople.NameOfAsync(dbContext, requestContext));
        return new PortalPersonAdded
        {
            Id = account.Id,
            Emailed = invite.Emailed,
            EmailError = invite.EmailError,
            ExpiresOn = invite.ExpiresOn,
            Link = invite.Link
        };
    }

    private class Validate : AbstractValidator<PortalAddPerson>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            RuleFor(i => i.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(i => i.RoleId).GreaterThan(0);
        }
    }
}

/// <summary>Sends someone still to choose a password a new invite; the last one stops working.</summary>
[HandlerName("invitepersonagain")]
public class InvitePersonAgain(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, AccountMail mail)
    : ICommandHandler<PortalPersonRequest, object>
{
    public async Task<object> Handle(PortalPersonRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var account = await PortalPeople.ChangeableAsync(dbContext, requestContext, caller, request.AccountId);
        if (account.Disabled)
            throw new SWValidationException("ACCOUNT_DISABLED", $"{account.DisplayName} is disabled. Enable them first.");
        return await Invite.SendAsync(dbContext, options, mail, account, await PortalPeople.NameOfAsync(dbContext, requestContext));
    }
}

/// <summary>Gives someone at their company another role.</summary>
[HandlerName("setpersonrole")]
public class SetPersonRole(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalPersonRole, object>
{
    public async Task<object> Handle(PortalPersonRole request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var account = await PortalPeople.ChangeableAsync(dbContext, requestContext, caller, request.AccountId);
        await PortalPeople.EnsureGrantable(dbContext, caller, request.RoleId);

        await AccountRoles.Set(dbContext, account.Id, [request.RoleId]);
        account.SetRole(AccountRoles.LegacyRoleFor([request.RoleId]));
        await dbContext.SaveChangesAsync();
        return null;
    }
}

/// <summary>Stops someone at their company signing in, or lets them again.</summary>
[HandlerName("setpersondisabled")]
public class SetPersonDisabled(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalPersonDisabled, object>
{
    public async Task<object> Handle(PortalPersonDisabled request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var account = await PortalPeople.ChangeableAsync(dbContext, requestContext, caller, request.AccountId);
        account.SetDisabled(request.Disabled);
        await dbContext.SaveChangesAsync();

        // As on our team's page: a refresh token signs in again without a password, so it goes too.
        if (request.Disabled)
            await dbContext.Set<RefreshToken>().Where(t => t.AccountId == account.Id).ExecuteDeleteAsync();
        return null;
    }
}

/// <summary>Removes someone at their company for good.</summary>
[HandlerName("removeperson")]
public class RemovePerson(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalPersonRequest, object>
{
    public async Task<object> Handle(PortalPersonRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PeopleEdit);
        var account = await PortalPeople.ChangeableAsync(dbContext, requestContext, caller, request.AccountId);
        dbContext.Remove(account);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
