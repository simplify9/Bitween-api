using System;

namespace SW.Infolink.Model;

public class CreateAccountModel
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public int Role { get; set; }
}

public class UpdateAccountModel
{
    public string Name { get; set; }
    public int Role { get; set; }
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

public class AccountModel
{
    public string Name { get; set; }
    public int Id { get; set; }
    public string Email { get; set; }

    public string Role { get; set; }
    public DateTime CreatedOn { get; set; }
}

public class ChangePasswordModel
{
    public string NewPassword { get; set; }

    public string OldPassword { get; set; }
}