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
    private static readonly string[] NonAdminGroups = ["Operate", "Subscriptions", "Configuration"];

    private Role()
    {
    }

    /// <summary>Seeding path for the built-in roles - fixed Id, grants computed at runtime.</summary>
    public Role(int id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
        IsSystem = true;
    }

    /// <summary>
    /// A built-in partner role, made on startup where it is missing (see BuiltInPartnerRoles). Its
    /// id is whatever the database gives it, which is why it is known by <see cref="PresetKey"/>:
    /// a fixed id could already belong to a custom role on an existing installation.
    /// </summary>
    public static Role ForPreset(PartnerPreset preset, string name) => new()
    {
        Name = name,
        Description = preset.Description,
        IsSystem = true,
        ForPartners = true,
        PresetKey = preset.Key,
    };

    public Role(string name, string description, IEnumerable<string> permissions, bool forPartners = false)
    {
        Name = name;
        Description = description;
        Permissions = PermissionCatalog.Sanitize(permissions);
        ForPartners = forPartners;
    }

    public string Name { get; private set; }
    public string Description { get; private set; }

    /// <summary>
    /// Catalog keys, stored as one JSON column. Empty for built-in roles - see
    /// <see cref="GetEffectivePermissions"/>.
    /// </summary>
    public List<string> Permissions { get; private set; } = [];

    /// <summary>Built-in roles can be assigned, but never edited or deleted.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// A role for a partner's own people, holding only partner portal permissions. Given only to
    /// accounts that belong to a partner, who hold no other kind. Fixed when the role is created,
    /// so a role already held can never change which kind of account it suits.
    /// </summary>
    public bool ForPartners { get; private set; }

    /// <summary>Which built-in partner role this is; null for every other role, built-in or custom.</summary>
    public string PresetKey { get; private set; }

    /// <summary>
    /// The built-in partner roles. Their grants are worked out from the catalog on every read, as
    /// Administrator's are, so a permission added to the portal later reaches them unasked.
    /// </summary>
    public static readonly IReadOnlyList<PartnerPreset> PartnerPresets =
    [
        new("partner-viewer", "Partner viewer",
            "For a partner's people: sees that partner's subscriptions, exchanges and mappings, with the exchanges' files and errors.",
            () => ButPeople(PermissionCatalog.InGroups(true, PermissionCatalog.PortalGroup))),
        new("partner-editor", "Partner editor",
            "For a partner's people: everything Partner viewer sees, and changes their own mappings.",
            () => ButPeople(PermissionCatalog.InGroups(false, PermissionCatalog.PortalGroup))),
        new("partner-admin", "Partner admin",
            "For a partner's people: everything Partner editor does, and invites and manages their company's people. Only your team makes one.",
            () => PermissionCatalog.InGroups(false, PermissionCatalog.PortalGroup)),
    ];

    /// <summary>Without managing people: that is what makes a partner's person one of their admins.</summary>
    private static List<string> ButPeople(List<string> keys) =>
        keys.Where(k => !k.StartsWith(PermissionCatalog.PortalPeopleArea + ".")).ToList();

    /// <summary>For a built-in role whose wording changed in a later version.</summary>
    public void Describe(string description) => Description = description;

    public void Update(string name, string description, IEnumerable<string> permissions)
    {
        Name = name;
        Description = description;
        Permissions = PermissionCatalog.Sanitize(permissions);
    }

    /// <summary>
    /// What this role actually grants. Built-in roles derive their grants from the catalog at
    /// runtime instead of storing them, so adding a permission to the catalog reaches
    /// Administrators - and the right groups - without a migration or a data fix-up.
    /// </summary>
    public List<string> GetEffectivePermissions() => IsSystem ? BuiltInPermissions(Id, PresetKey) : Permissions;

    /// <summary>What a built-in role grants: a partner preset by its key, any other by its id.</summary>
    public static List<string> BuiltInPermissions(int roleId, string presetKey) =>
        presetKey is null
            ? SystemPermissions(roleId)
            : PartnerPresets.FirstOrDefault(p => p.Key == presetKey)?.Permissions() ?? [];

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

/// <summary>A built-in partner role: its key, the name it is made with, and what it grants.</summary>
public record PartnerPreset(string Key, string Name, string Description, Func<List<string>> Permissions);
