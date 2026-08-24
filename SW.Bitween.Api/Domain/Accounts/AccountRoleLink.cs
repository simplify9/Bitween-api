namespace SW.Bitween.Domain.Accounts;

/// <summary>Which roles an account holds. Composite key — one row per account/role pair.</summary>
public class AccountRoleLink
{
    private AccountRoleLink()
    {
    }

    public AccountRoleLink(int accountId, int roleId)
    {
        AccountId = accountId;
        RoleId = roleId;
    }

    public int AccountId { get; private set; }
    public int RoleId { get; private set; }
}
