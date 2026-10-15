using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The bell a sync gateway call waits on for its exchange's result, and the backstop poll beside it.
/// The waits run on a <see cref="ManualClock"/>, so what they see does not depend on how busy the
/// machine running the tests is.
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

    static Func<Task<XchangeResultReady>> Never => () => Task.FromResult<XchangeResultReady>(null);

    [TestMethod]
    public async Task A_signal_answers_without_asking_the_database()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        using var wait = signals.Register("x5", TimeSpan.FromMinutes(1));
        var checks = 0;

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result, () => { checks++; return Task.FromResult<XchangeResultReady>(null); },
            30, CancellationToken.None, time: clock);
        await clock.Settle(waiting, since);
        signals.Complete(Ready("x5"));

        Assert.AreEqual("x5", (await waiting)?.Id);
        Assert.AreEqual(0, checks);
        Assert.AreEqual(TimeSpan.Zero, clock.Now);
    }

    [TestMethod]
    public async Task Without_a_signal_the_backstop_finds_the_result()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        using var wait = signals.Register("x6", TimeSpan.FromMinutes(1));
        var checkedAt = new List<TimeSpan>();

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result,
            () => { checkedAt.Add(clock.Now); return Task.FromResult(checkedAt.Count == 2 ? Ready("x6") : null); },
            30, CancellationToken.None, firstCheck: TimeSpan.FromMilliseconds(100),
            interval: TimeSpan.FromMilliseconds(100), time: clock);
        while (!waiting.IsCompleted)
        {
            await clock.Settle(waiting, since);
            if (waiting.IsCompleted) break;
            since = clock.Created;
            clock.FireNext();
        }

        Assert.AreEqual("x6", (await waiting)?.Id);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200) }, checkedAt);
    }

    [TestMethod]
    public async Task The_backstop_waits_two_seconds_before_its_first_check()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        using var wait = signals.Register("x7", TimeSpan.FromMinutes(1));
        TimeSpan? firstCheck = null;

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result,
            () => { firstCheck ??= clock.Now; return Task.FromResult(Ready("x7")); }, 30, CancellationToken.None,
            time: clock);
        await clock.Settle(waiting, since);
        clock.FireNext();

        Assert.AreEqual("x7", (await waiting)?.Id);
        Assert.AreEqual(ResultWait.BackstopFirstCheck, firstCheck);
        Assert.AreEqual(TimeSpan.FromSeconds(2), firstCheck);
    }

    [TestMethod]
    public async Task A_signal_that_expires_leaves_the_backstop_to_answer()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        var wait = signals.Register("x8", TimeSpan.FromMinutes(1));
        TimeSpan? checkedAt = null;

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result,
            () => { checkedAt = clock.Now; return Task.FromResult(Ready("x8")); }, 30, CancellationToken.None,
            firstCheck: TimeSpan.FromMilliseconds(200), time: clock);
        await clock.Settle(waiting, since);

        // An expiring wait and a disposed one end the bell the same way, cancelled; disposing it
        // ends it at a moment of the test's choosing.
        clock.Pass(TimeSpan.FromMilliseconds(50));
        since = clock.Created;
        wait.Dispose();
        await clock.Settle(waiting, since);
        clock.FireNext();

        Assert.AreEqual("x8", (await waiting)?.Id);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), checkedAt);
    }

    [TestMethod]
    public async Task A_signal_that_ends_after_the_check_was_due_leaves_the_backstop_to_answer()
    {
        // The clock passed the backstop's check while the signal was still pending, and the signal
        // then ended without a result. The wait until that check is negative; Task.Delay threw on it
        // and the call failed instead of asking the database.
        var signals = new ResultSignals();
        var clock = new ManualClock();
        var wait = signals.Register("x10", TimeSpan.FromMinutes(1));
        TimeSpan? checkedAt = null;

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result,
            () => { checkedAt = clock.Now; return Task.FromResult(Ready("x10")); }, 30, CancellationToken.None,
            firstCheck: TimeSpan.FromMilliseconds(200), time: clock);
        await clock.Settle(waiting, since);

        clock.Pass(TimeSpan.FromMilliseconds(300));
        wait.Dispose();

        Assert.AreEqual("x10", (await waiting)?.Id);
        Assert.AreEqual(TimeSpan.FromMilliseconds(300), checkedAt);
    }

    [TestMethod]
    public async Task The_wait_ends_on_time()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        using var wait = signals.Register("x9", TimeSpan.FromMinutes(1));

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result, Never, 1, CancellationToken.None, time: clock);
        await clock.Settle(waiting, since);
        clock.FireNext();

        Assert.IsNull(await waiting);
        Assert.AreEqual(TimeSpan.FromSeconds(1), clock.Now);
    }

    [TestMethod]
    public async Task The_wait_ends_when_the_caller_goes_away()
    {
        var signals = new ResultSignals();
        var clock = new ManualClock();
        using var wait = signals.Register("x11", TimeSpan.FromMinutes(1));
        using var gone = new CancellationTokenSource();

        var since = clock.Created;
        var waiting = ResultWait.UntilAsync(wait.Result, Never, 60, gone.Token, time: clock);
        await clock.Settle(waiting, since);
        gone.Cancel();

        Assert.IsNull(await waiting);
        Assert.AreEqual(TimeSpan.Zero, clock.Now);
    }
}
