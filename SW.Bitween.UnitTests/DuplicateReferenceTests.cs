using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>The duplicate-of reference is only added where it fits the column references are stored in.</summary>
[TestClass]
public class DuplicateReferenceTests
{
    static readonly DuplicateVerdict Duplicate = new(new string('a', 32), Drop: false);

    [TestMethod]
    public void It_is_added_beside_ordinary_references() =>
        CollectionAssert.Contains(XchangeService.WithDuplicateReference(["partnerkey: acme"], Duplicate), Duplicate.Reference);

    [TestMethod]
    public void It_is_left_off_rather_than_overflow_the_column()
    {
        var full = Enumerable.Range(0, 10).Select(i => new string('x', 100)).ToArray();
        Assert.IsFalse(Duplicate.FitsWith(full));
        CollectionAssert.AreEqual(full, XchangeService.WithDuplicateReference(full, Duplicate));
    }

    [TestMethod]
    public void No_duplicate_changes_nothing() =>
        CollectionAssert.AreEqual(new[] { "a" }, XchangeService.WithDuplicateReference(["a"], null));
}
