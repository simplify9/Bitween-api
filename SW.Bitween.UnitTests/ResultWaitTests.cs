using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The synchronous gateway wait used to compare its polling step, capped at eight seconds, with the
/// requested wait — so a wait of eight seconds or more never ended.
/// </summary>
[TestClass]
public class ResultWaitTests
{
    [DataTestMethod]
    [DataRow(null, 300, 120)]
    [DataRow(0, 300, 120)]
    [DataRow(-5, 300, 120)]
    [DataRow(30, 300, 30)]
    [DataRow(100000, 300, 300)]
    public void A_requested_wait_is_defaulted_and_capped(int? requested, int max, int expected) =>
        Assert.AreEqual(expected, ResultWait.Clamp(requested, max));

    [TestMethod]
    public async Task A_result_that_never_comes_ends_the_wait_on_time()
    {
        var clock = Stopwatch.StartNew();
        Assert.IsFalse(await ResultWait.UntilAsync(() => Task.FromResult(false), 2, CancellationToken.None));
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(4), $"took {clock.Elapsed}");
    }

    [TestMethod]
    public async Task A_result_ends_the_wait_as_soon_as_it_is_there() =>
        Assert.IsTrue(await ResultWait.UntilAsync(() => Task.FromResult(true), 60, CancellationToken.None));

    [TestMethod]
    public async Task The_wait_ends_when_the_caller_goes_away()
    {
        using var gone = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();
        Assert.IsFalse(await ResultWait.UntilAsync(() => Task.FromResult(false), 60, gone.Token));
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
    }
}
