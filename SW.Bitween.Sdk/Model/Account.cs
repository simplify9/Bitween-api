using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class CreateAccountModel
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }

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
    public string Name { get; set; }

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
    public string Name { get; set; }
}

public class AccountModel
{
    public string Name { get; set; }
    public int Id { get; set; }
    public string Email { get; set; }

    /// <summary>
    /// Legacy coarse role, kept for older clients. Authorization reads <see cref="Roles"/>.
    /// </summary>
    public string Role { get; set; }

    public bool Disabled { get; set; }
    public DateTime CreatedOn { get; set; }
    public List<AccountRoleSummary> Roles { get; set; } = [];
}

/// <summary>The signed-in account, plus everything the UI needs to decide what to show.</summary>
public class ProfileModel : AccountModel
{
    public List<string> Permissions { get; set; } = [];
}

public class ChangePasswordModel
{
    public string NewPassword { get; set; }

    public string OldPassword { get; set; }
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
