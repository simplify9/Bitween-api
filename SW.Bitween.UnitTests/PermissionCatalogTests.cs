using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

[TestClass]
public class PermissionCatalogTests
{
    /// <summary>Every key declared as a constant on <see cref="Permissions"/>.</summary>
    private static List<string> ConstantKeys() =>
        typeof(Permissions).GetNestedTypes()
            .SelectMany(area => area.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue())
            .ToList();

    [TestMethod]
    public void Constants_AndCatalog_DescribeTheSameKeys()
    {
        var constants = ConstantKeys();

        // A constant with no catalog entry can never be granted, so the handler guarding on it
        // would deny everyone. A catalog entry with no constant is a grant nothing enforces.
        CollectionAssert.AreEquivalent(
            PermissionCatalog.AllKeys.ToList(),
            constants,
            "Permissions constants and PermissionCatalog have drifted apart.");
    }

    [TestMethod]
    public void EveryKey_IsAreaDotAction()
    {
        foreach (var key in PermissionCatalog.AllKeys)
            Assert.AreEqual(2, key.Split('.').Length, $"'{key}' is not in <area>.<action> form.");
    }

    [TestMethod]
    public void Constants_HaveNoDuplicates()
    {
        var constants = ConstantKeys();
        CollectionAssert.AreEquivalent(constants.Distinct().ToList(), constants);
    }

    [TestMethod]
    public void Administrator_GrantsEveryPermission()
    {
        // Derived from the catalog rather than stored, so a newly added permission reaches
        // administrators without a migration. Guards that behaviour.
        CollectionAssert.AreEquivalent(
            PermissionCatalog.AllKeys.ToList(),
            Role.SystemPermissions(Role.AdministratorId));
    }

    [TestMethod]
    public void MemberAndViewer_CannotReachAdministration()
    {
        var administration = PermissionCatalog.Areas
            .Where(a => a.Group == "Administration")
            .SelectMany(a => a.Actions.Select(x => $"{a.Id}.{x.Id}"))
            .ToList();

        foreach (var roleId in new[] { Role.MemberId, Role.ViewerId })
        {
            var granted = Role.SystemPermissions(roleId);
            Assert.IsFalse(granted.Intersect(administration).Any(),
                $"Built-in role {roleId} must not grant members/roles/settings access.");
            Assert.IsTrue(granted.Count > 0);
        }
    }

    [TestMethod]
    public void Viewer_GrantsViewOnly()
    {
        Assert.IsTrue(Role.SystemPermissions(Role.ViewerId).All(k => k.EndsWith(".view")));
    }

    [TestMethod]
    public void Member_CanWriteIntegrationsButNotOnlyView()
    {
        var granted = Role.SystemPermissions(Role.MemberId);
        Assert.IsTrue(granted.Contains(Permissions.Subscriptions.Edit));
        Assert.IsTrue(granted.Contains(Permissions.Subscriptions.View));
        Assert.IsFalse(granted.Contains(Permissions.Users.View));
    }

    [TestMethod]
    public void Sanitize_DropsKeysOutsideTheCatalog()
    {
        var cleaned = PermissionCatalog.Sanitize([
            Permissions.Partners.View, "partners.teleport", "", Permissions.Partners.View
        ]);

        CollectionAssert.AreEqual(new List<string> { Permissions.Partners.View }, cleaned);
    }

    [TestMethod]
    public void CustomRole_GrantsExactlyWhatItStores()
    {
        var role = new Role("Support", "Reads exchanges", [Permissions.Exchanges.View, "nope.view"]);
        CollectionAssert.AreEqual(new List<string> { Permissions.Exchanges.View },
            role.GetEffectivePermissions());
    }
}
