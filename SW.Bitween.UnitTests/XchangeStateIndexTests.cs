using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.XchangeStates;

namespace SW.Bitween.UnitTests;

/// <summary>
/// SQL Server builds the exchange status index straight away only where its edition can do it
/// without pausing writes; the rest wait for the quiet hour.
/// </summary>
[TestClass]
public class XchangeStateIndexTests
{
    [DataTestMethod]
    [DataRow(3, true, DisplayName = "Enterprise and Developer")]
    [DataRow(5, true, DisplayName = "Azure SQL Database")]
    [DataRow(8, true, DisplayName = "Managed Instance")]
    [DataRow(2, false, DisplayName = "Standard")]
    [DataRow(4, false, DisplayName = "Express")]
    public void Only_editions_that_build_online_skip_the_quiet_hour(int engineEdition, bool online) =>
        Assert.AreEqual(online, XchangeStateIndex.BuildsOnline(engineEdition));
}
