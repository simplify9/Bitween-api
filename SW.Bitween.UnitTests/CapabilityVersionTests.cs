using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Adapters.Db;
using SW.Bitween.Adapters.Db.PostgreSql;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A capability list is written per engine and is therefore about the engine at its NEWEST. Where
/// a feature arrived in a specific release, the list has to be narrowed once the server version is
/// known - otherwise it promises something an older server will refuse, and the promise is found
/// out on the first message rather than on the screen that made it.
/// </summary>
[TestClass]
public class CapabilityVersionTests
{
    [TestMethod]
    public void Merge_is_claimed_on_PostgreSQL_15_and_later()
    {
        Assert.IsTrue(MergeFor("15.6"));
        Assert.IsTrue(MergeFor("16.14"));
        Assert.IsTrue(MergeFor("17.2"));
    }

    [TestMethod]
    public void Merge_is_withdrawn_on_PostgreSQL_before_15()
    {
        // 13 and 14 are both still widely run and were supported into 2025 and 2026.
        Assert.IsFalse(MergeFor("13.14"));
        Assert.IsFalse(MergeFor("14.11"));
    }

    [TestMethod]
    public void An_unreadable_version_leaves_the_declared_list_alone()
    {
        // Better to claim what the engine can do at its newest than to strip a capability because
        // a driver reported the version in a shape this did not expect.
        Assert.IsTrue(MergeFor(null));
        Assert.IsTrue(MergeFor(""));
        Assert.IsTrue(MergeFor("not a version"));
    }

    static bool MergeFor(string serverVersion)
    {
        var described = new DbCapabilities { Merge = true, ServerVersion = serverVersion };
        PostgreSqlDbAdapter.AdjustForVersionForTests(described);
        return described.Merge;
    }
}
