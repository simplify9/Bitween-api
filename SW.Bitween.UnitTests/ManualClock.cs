using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.UnitTests;

/// <summary>
/// A clock that moves only when a test moves it, so a wait can be stepped through without sleeping:
/// <see cref="Settle"/> until the code under test is asleep or done, then fire the next timer.
/// </summary>
sealed class ManualClock : TimeProvider
{
    private readonly object gate = new();
    private readonly List<Timer> timers = new();
    private long now;
    private int created;

    public TimeSpan Now
    {
        get { lock (gate) return TimeSpan.FromTicks(now); }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (gate) return now;
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + Now;

    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        lock (gate)
        {
            created++;
            timers.Add(timer);
        }
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock without firing anything, as on a machine too busy to run timers on time:
    /// whatever wakes the code next finds the time already later than it planned for.
    /// </summary>
    public void Pass(TimeSpan by)
    {
        lock (gate) now += by.Ticks;
    }

    /// <summary>Moves the clock to the earliest timer and fires it.</summary>
    public void FireNext()
    {
        Timer next;
        lock (gate)
        {
            next = timers.Where(t => t.Due != null).MinBy(t => t.Due);
            if (next == null) throw new InvalidOperationException("Nothing is waiting on the clock.");
            now = Math.Max(now, next.Due.Value);
            next.Due = null;
        }
        next.Callback(next.State);
    }

    /// <summary>
    /// Waits for <paramref name="work"/> to finish or to go back to sleep on a timer it set after
    /// <paramref name="since"/> (a count of timers from <see cref="Created"/>). The real-time guard
    /// only stops a broken test from hanging; it plays no part in what the test sees.
    /// </summary>
    public async Task Settle(Task work, int since)
    {
        var guard = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!work.IsCompleted)
        {
            lock (gate)
                if (created > since && timers.Any(t => t.Due != null)) return;
            if (DateTime.UtcNow > guard) throw new TimeoutException("The work neither finished nor slept.");
            await Task.Yield();
        }
    }

    public int Created
    {
        get { lock (gate) return created; }
    }

    private sealed class Timer(ManualClock clock, TimerCallback callback, object state) : ITimer
    {
        public TimerCallback Callback => callback;
        public object State => state;
        public long? Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime.Ticks;
            return true;
        }

        public void Dispose()
        {
            lock (clock.gate)
            {
                Due = null;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
