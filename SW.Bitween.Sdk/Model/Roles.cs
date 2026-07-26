using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

public class RoleCreate
{
    public required string Name { get; set; }
    public string Description { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public class RoleUpdate : RoleCreate;

/// <summary>One shape for both the roles list and a single role — the UI shows the same fields.</summary>
public class RoleRow
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>Built-in roles can be assigned, but not edited or deleted.</summary>
    public bool IsSystem { get; set; }

    public List<string> Permissions { get; set; } = [];
    public int MemberCount { get; set; }
    public DateTime CreatedOn { get; set; }
}
