using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class CreateAccountModel
{
    /// <summary>Name and Email are required; the server rejects a create without them.</summary>
    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    /// <summary>Null for an account that signs in with Microsoft rather than a password.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Legacy coarse role. Nullable on purpose: when it was a plain int, a request that omitted it
    /// sent 0 - which is Admin - so a member added with no roles came out an administrator.
    /// </summary>
    public int? Role { get; set; }

    /// <summary>Roles to grant. Preferred over <see cref="Role"/>, which is legacy.</summary>
    public List<int> RoleIds { get; set; } = [];

    /// <summary>
    /// The member chooses their own password from an invite link instead, so none is sent. The
    /// invite itself is <c>POST /accounts/{id}/invite</c>.
    /// </summary>
    public bool Invite { get; set; }
}

public class UpdateAccountModel
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Legacy coarse role. Nullable on purpose: when it was a plain int, a request that omitted it
    /// sent 0 - which is Admin. Only supplied values are applied, and only with users.edit.
    /// </summary>
    public int? Role { get; set; }
}

public class RemoveAccountModel
{
}

public class SearchMembersModel
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public bool Lookup { get; set; }
}

public class AccountRoleSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}

public class AccountModel
{
    public string Name { get; set; } = null!;
    public int Id { get; set; }
    public string Email { get; set; } = null!;

    /// <summary>
    /// Legacy coarse role, kept for older clients. Authorization reads <see cref="Roles"/>.
    /// </summary>
    public string? Role { get; set; }

    public bool Disabled { get; set; }
    public DateTime CreatedOn { get; set; }

    /// <summary>When the member last signed in (password, Microsoft or the CLI); null if never since this was kept.</summary>
    public DateTime? LastSignInOn { get; set; }

    /// <summary>Whether a Microsoft identity is bound to the account.</summary>
    public bool MicrosoftLinked { get; set; }

    public List<AccountRoleSummary> Roles { get; set; } = [];

    // Non-null and in the future => the account is currently locked out.
    public DateTime? LockoutEnd { get; set; }

    /// <summary>Whether the member has a password. One invited and yet to choose it hasn't.</summary>
    public bool HasPassword { get; set; }

    /// <summary>The member's latest invite while they are yet to choose a password; null otherwise.</summary>
    public AccountInviteModel? Invite { get; set; }
}

/// <summary>An invite waiting to be used.</summary>
public class AccountInviteModel
{
    public DateTime ExpiresOn { get; set; }

    /// <summary>When it was emailed; null when it wasn't, and the link was handed to whoever invited.</summary>
    public DateTime? EmailedOn { get; set; }

    /// <summary>Why the email couldn't be sent, when it couldn't.</summary>
    public string? EmailError { get; set; }
}

public class InviteAccountModel
{
}

/// <summary>What became of an invite.</summary>
public class InviteResultModel
{
    /// <summary>Whether it was emailed to the member.</summary>
    public bool Emailed { get; set; }

    /// <summary>Why it couldn't be, when there is a mail server and sending failed.</summary>
    public string? EmailError { get; set; }

    /// <summary>When the link stops working; null for an instance without passwords, whose invite has no link.</summary>
    public DateTime? ExpiresOn { get; set; }

    /// <summary>
    /// The link, only when it wasn't emailed, for whoever invited to pass on. Shown once: Bitween
    /// keeps only its hash.
    /// </summary>
    public string? Link { get; set; }
}

/// <summary>"Forgot password?" on the sign-in page.</summary>
public class ForgotPasswordModel
{
    public string Email { get; set; } = null!;
}

/// <summary>A link from an invite or reset email, opened.</summary>
public class PasswordLinkModel
{
    public string Token { get; set; } = null!;
}

/// <summary>Who a password link is for, for the page that uses it.</summary>
public class PasswordLinkInfoModel
{
    /// <summary><c>Invite</c> or <c>PasswordReset</c>.</summary>
    public string Purpose { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public DateTime ExpiresOn { get; set; }
}

/// <summary>A password chosen from an invite or reset link.</summary>
public class UsePasswordLinkModel
{
    public string Token { get; set; } = null!;
    public string Password { get; set; } = null!;
}

/// <summary>The signed-in account, plus everything the UI needs to decide what to show.</summary>
public class ProfileModel : AccountModel
{
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// The account still has the password it was created with, so its token grants nothing until
    /// that is changed. Here as well as on the sign-in response because a reload has no sign-in
    /// response to read - without it, refreshing the page lands on an app with every screen denied
    /// and no explanation.
    /// </summary>
    public bool MustChangePassword { get; set; }
}

public class UnlockAccountModel
{
}

public class ChangePasswordModel
{
    public string NewPassword { get; set; } = null!;

    public string OldPassword { get; set; } = null!;
}

/// <summary>Replaces the whole set of roles a member holds.</summary>
public class SetAccountRolesModel
{
    public List<int> RoleIds { get; set; } = [];
}

public class SetAccountDisabledModel
{
    public bool Disabled { get; set; }
}

/// <summary>An administrator setting someone else's password, standing in for a reset flow.</summary>
public class SetAccountPasswordModel
{
    public string Password { get; set; } = null!;
}
