using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The bell a sync gateway call waits on for its exchange's result, and the backstop poll beside it
/// (finding 2 of docs/design/processing-performance.md).
/// </summary>
[TestClass]
public class ResultSignalsTests
{
    static XchangeResultReady Ready(string id) => new() { Id = id, Success = true };

    [TestMethod]
    public async Task A_result_rung_after_the_call_registered_reaches_it()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x1", TimeSpan.FromMinutes(1));

        Assert.IsTrue(signals.Complete(Ready("x1")));

        Assert.AreEqual("x1", (await wait.Result).Id);
        Assert.AreEqual(0, signals.Count);
    }

    [TestMethod]
    public void A_result_rung_before_anyone_registered_is_dropped()
    {
        var signals = new ResultSignals();
        Assert.IsFalse(signals.Complete(Ready("x2")));

        using var wait = signals.Register("x2", TimeSpan.FromMinutes(1));
        Assert.IsFalse(wait.Result.IsCompleted);
    }

    [TestMethod]
    public async Task A_wait_nobody_answers_expires_and_leaves_nothing_behind()
    {
        var signals = new ResultSignals();
        var wait = signals.Register("x3", TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => wait.Result);
        Assert.AreEqual(0, signals.Count);
        Assert.IsFalse(signals.Complete(Ready("x3")));
    }

    [TestMethod]
    public void A_wait_given_up_leaves_nothing_behind()
    {
        var signals = new ResultSignals();
        var wait = signals.Register("x4", TimeSpan.FromMinutes(1));
        wait.Dispose();

        Assert.AreEqual(0, signals.Count);
        Assert.IsTrue(wait.Result.IsCanceled);
    }

    [TestMethod]
    public void Every_instance_has_a_name_of_its_own()
    {
        Assert.AreNotEqual(new ResultSignals().Node, new ResultSignals().Node);
    }

    [TestMethod]
    public async Task A_signal_answers_without_asking_the_database()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x5", TimeSpan.FromMinutes(1));
        var checks = 0;
        _ = Task.Delay(50).ContinueWith(_ => signals.Complete(Ready("x5")));

        var clock = Stopwatch.StartNew();
        var ready = await ResultWait.UntilAsync(wait.Result, () => { checks++; return Task.FromResult<XchangeResultReady>(null); },
            30, CancellationToken.None);

        Assert.AreEqual("x5", ready?.Id);
        Assert.AreEqual(0, checks);
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");
    }

    [TestMethod]
    public async Task Without_a_signal_the_backstop_finds_the_result()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x6", TimeSpan.FromMinutes(1));
        var checks = 0;

        var clock = Stopwatch.StartNew();
        var ready = await ResultWait.UntilAsync(wait.Result,
            () => Task.FromResult(++checks == 2 ? Ready("x6") : null), 30, CancellationToken.None,
            firstCheck: TimeSpan.FromMilliseconds(100), interval: TimeSpan.FromMilliseconds(100));

        Assert.AreEqual("x6", ready?.Id);
        Assert.AreEqual(2, checks);
        Assert.IsTrue(clock.Elapsed >= TimeSpan.FromMilliseconds(190), $"took {clock.Elapsed}");
    }

    [TestMethod]
    public async Task The_backstop_waits_two_seconds_before_its_first_check()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x7", TimeSpan.FromMinutes(1));
        TimeSpan? firstCheck = null;
        var clock = Stopwatch.StartNew();

        var ready = await ResultWait.UntilAsync(wait.Result,
            () => { firstCheck ??= clock.Elapsed; return Task.FromResult(Ready("x7")); }, 30, CancellationToken.None);

        Assert.AreEqual("x7", ready?.Id);
        Assert.IsTrue(firstCheck >= TimeSpan.FromSeconds(1.9) && firstCheck < TimeSpan.FromSeconds(3), $"first check at {firstCheck}");
    }

    [TestMethod]
    public async Task A_signal_that_expires_leaves_the_backstop_to_answer()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x8", TimeSpan.FromMilliseconds(50));

        var ready = await ResultWait.UntilAsync(wait.Result, () => Task.FromResult(Ready("x8")), 30,
            CancellationToken.None, firstCheck: TimeSpan.FromMilliseconds(200));

        Assert.AreEqual("x8", ready?.Id);
    }

    [TestMethod]
    public async Task The_wait_ends_on_time_and_when_the_caller_goes_away()
    {
        var signals = new ResultSignals();
        using var wait = signals.Register("x9", TimeSpan.FromMinutes(1));
        Func<Task<XchangeResultReady>> never = () => Task.FromResult<XchangeResultReady>(null);

        var clock = Stopwatch.StartNew();
        Assert.IsNull(await ResultWait.UntilAsync(wait.Result, never, 1, CancellationToken.None));
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");

        using var gone = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        clock.Restart();
        Assert.IsNull(await ResultWait.UntilAsync(wait.Result, never, 60, gone.Token));
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");
    }
}
