using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// How a subscription's test is summed up for the lists: the worst of its arrivals, an arrival that
/// didn't run counting as one to check.
/// </summary>
[TestClass]
public class MirrorTestVerdictTests
{
    static readonly DateTimeOffset RanOn = DateTimeOffset.UtcNow;

    static MirrorTestVerdict Of(params string[] verdicts) =>
        MirrorService.VerdictOf(verdicts.Select((v, i) => new MirrorFamilyRun($"f{i}", $"c{i}", RanOn, v, v == null ? "It didn't run." : null, [], []))
            .ToList(), RanOn);

    [TestMethod]
    public void Every_arrival_the_same_is_the_same()
    {
        var verdict = Of("same", "same");

        Assert.AreEqual("same", verdict.Verdict);
        Assert.AreEqual(2, verdict.Same);
        Assert.AreEqual(RanOn, verdict.RanOn);
    }

    [TestMethod]
    public void One_to_check_or_one_that_didnt_run_is_to_check()
    {
        Assert.AreEqual("check", Of("same", "check").Verdict);

        var notRun = Of("same", null);
        Assert.AreEqual("check", notRun.Verdict);
        Assert.AreEqual(1, notRun.NotRun);
    }

    [TestMethod]
    public void One_different_is_different_whatever_the_rest()
    {
        var verdict = Of("check", "different", null, "same");

        Assert.AreEqual("different", verdict.Verdict);
        Assert.AreEqual((1, 1, 1, 1), (verdict.Same, verdict.Check, verdict.Different, verdict.NotRun));
    }
}
