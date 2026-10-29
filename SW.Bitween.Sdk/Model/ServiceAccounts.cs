using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>Trades a service token for a session: <c>POST /api/accounts/servicetoken</c>.</summary>
public class ServiceTokenSignInModel
{
    public string Token { get; set; } = null!;
}

public class SearchServiceAccountsModel
{
}

public class CreateServiceAccountModel
{
    public string Name { get; set; } = null!;
    public List<int> RoleIds { get; set; } = [];
}

public class UpdateServiceAccountModel
{
    public string Name { get; set; } = null!;
    public List<int> RoleIds { get; set; } = [];
}

public class SetServiceAccountDisabledModel
{
    public bool Disabled { get; set; }
}

public class CreateServiceTokenModel
{
    /// <summary>What the token is for, to tell it apart: "lab apply job".</summary>
    public string Name { get; set; } = null!;

    /// <summary>1 to 365; 90 when left out.</summary>
    public int? ExpiresInDays { get; set; }
}

public class RevokeServiceTokenModel
{
    public int TokenId { get; set; }
}

public class ServiceTokenModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>The token's first characters, "bwt_3fK9aB2x", never the token.</summary>
    public string Shown { get; set; } = null!;

    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime ExpiresOn { get; set; }
    public DateTime? RevokedOn { get; set; }
    public DateTime? LastUsedOn { get; set; }
    public string? LastUsedFrom { get; set; }

    /// <summary>active, expired or revoked.</summary>
    public string Status { get; set; } = null!;
}

public class ServiceAccountModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool Disabled { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? LastSignInOn { get; set; }
    public List<AccountRoleSummary> Roles { get; set; } = [];
    public List<ServiceTokenModel> Tokens { get; set; } = [];
}

/// <summary>A token just made: the only answer that ever carries its value.</summary>
public class IssuedServiceTokenModel : ServiceTokenModel
{
    public string Token { get; set; } = null!;
}
