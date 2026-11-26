using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class RoleCreate
{
    public required string Name { get; set; }

    /// <summary>Free text shown beside the role; optional.</summary>
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// A role for a partner's own people, holding only partner portal permissions. Read when the
    /// role is created and fixed from then on; an update ignores it.
    /// </summary>
    public bool ForPartners { get; set; }
}

public class RoleUpdate : RoleCreate;

/// <summary>One shape for both the roles list and a single role - the UI shows the same fields.</summary>
public class RoleRow
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Built-in roles can be assigned, but not edited or deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>For a partner's own people; see <see cref="RoleCreate.ForPartners"/>.</summary>
    public bool ForPartners { get; set; }

    /// <summary>The partner whose admin made it, for their own people only; null for ours and built-in ones.</summary>
    public int? PartnerId { get; set; }
    public string? PartnerName { get; set; }

    public List<string> Permissions { get; set; } = [];
    public int MemberCount { get; set; }
    public DateTime CreatedOn { get; set; }
}
