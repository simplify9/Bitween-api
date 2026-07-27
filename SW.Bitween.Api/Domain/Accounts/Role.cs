using System;
using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts;

/// <summary>
/// A reusable set of permission keys. Members hold any number of roles and may do anything
/// any of their roles allows.
/// </summary>
public class Role : BaseEntity, IAudited
{
    public const int AdministratorId = 1;
    public const int MemberId = 2;
    public const int ViewerId = 3;

    /// <summary>The groups the built-in Member and Viewer roles reach; Administration stays admin-only.</summary>
    private static readonly string[] NonAdminGroups = ["Operate", "Integrations", "Configuration"];

    private Role()
    {
    }

    /// <summary>Seeding path for the built-in roles — fixed Id, grants computed at runtime.</summary>
    public Role(int id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
        IsSystem = true;
    }

    public Role(string name, string description, IEnumerable<string> permissions)
    {
        Name = name;
        Description = description;
        Permissions = PermissionCatalog.Sanitize(permissions);
    }

    public string Name { get; private set; }
    public string Description { get; private set; }

    /// <summary>
    /// Catalog keys, stored as one JSON column. Empty for built-in roles — see
    /// <see cref="GetEffectivePermissions"/>.
    /// </summary>
    public List<string> Permissions { get; private set; } = [];

    /// <summary>Built-in roles can be assigned, but never edited or deleted.</summary>
    public bool IsSystem { get; private set; }

    public void Update(string name, string description, IEnumerable<string> permissions)
    {
        Name = name;
        Description = description;
        Permissions = PermissionCatalog.Sanitize(permissions);
    }

    /// <summary>
    /// What this role actually grants. Built-in roles derive their grants from the catalog at
    /// runtime instead of storing them, so adding a permission to the catalog reaches
    /// Administrators — and the right groups — without a migration or a data fix-up.
    /// </summary>
    public List<string> GetEffectivePermissions() => IsSystem ? SystemPermissions(Id) : Permissions;

    public static List<string> SystemPermissions(int roleId) => roleId switch
    {
        AdministratorId => PermissionCatalog.AllKeys.ToList(),
        MemberId => PermissionCatalog.InGroups(false, NonAdminGroups),
        ViewerId => PermissionCatalog.InGroups(true, NonAdminGroups),
        _ => []
    };

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
