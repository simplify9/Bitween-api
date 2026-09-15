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
    /// sent 0 — which is Admin — so a member added with no roles came out an administrator.
    /// </summary>
    public int? Role { get; set; }

    /// <summary>Roles to grant. Preferred over <see cref="Role"/>, which is legacy.</summary>
    public List<int> RoleIds { get; set; } = [];
}

public class UpdateAccountModel
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Legacy coarse role. Nullable on purpose: when it was a plain int, a request that omitted it
    /// sent 0 — which is Admin. Only supplied values are applied, and only with users.edit.
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
    public List<AccountRoleSummary> Roles { get; set; } = [];

    // Non-null and in the future => the account is currently locked out.
    public DateTime? LockoutEnd { get; set; }
}

/// <summary>The signed-in account, plus everything the UI needs to decide what to show.</summary>
public class ProfileModel : AccountModel
{
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// The account still has the password it was created with, so its token grants nothing until
    /// that is changed. Here as well as on the sign-in response because a reload has no sign-in
    /// response to read — without it, refreshing the page lands on an app with every screen denied
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
