using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween;

/// <summary>
/// Holds a synchronous gateway call open while its exchange is processed: waiting for the worker's
/// signal with a poll of the database as the backstop, or, with no signal to wait on, polling.
/// </summary>
/// <remarks>
/// The loops this replaced compared the polling step - capped at eight seconds - with the requested
/// wait, so any wait of eight seconds or more, the default of 120 included, never ended. An exchange
/// that never got a result held its request, a DI scope and a database context for as long as the
/// process lived, and a caller could open as many as it liked. Now the wait is measured in elapsed
/// time, capped, and ends when the caller goes away.
/// </remarks>
public static class ResultWait
{
    public const int DefaultSeconds = 120;

    /// <summary>The wait a caller asked for, or the default, never above the configured ceiling.</summary>
    public static int Clamp(int? requested, int maxSeconds)
    {
        var seconds = requested is null or <= 0 ? DefaultSeconds : requested.Value;
        return Math.Min(seconds, Math.Max(1, maxSeconds));
    }

    /// <returns>True once <paramref name="isAvailable"/> says so; false when the time is up or the caller left.</returns>
    public static async Task<bool> UntilAsync(Func<Task<bool>> isAvailable, int seconds, CancellationToken cancellationToken)
    {
        var deadline = TimeSpan.FromSeconds(seconds);
        var clock = Stopwatch.StartNew();
        int step = 1, previous = 1;

        while (clock.Elapsed < deadline)
        {
            var remaining = deadline - clock.Elapsed;
            var delay = TimeSpan.FromSeconds(step) < remaining ? TimeSpan.FromSeconds(step) : remaining;
            try
            {
                await Task.Delay(NotNegative(delay), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (await isAvailable()) return true;

            (step, previous) = (Math.Min(step + previous, 8), step);
        }

        return false;
    }

    /// <summary>With a signal to wait on, the first check of the database comes this long after the call.</summary>
    public static readonly TimeSpan BackstopFirstCheck = TimeSpan.FromSeconds(2);

    /// <summary>With a signal to wait on, the later checks of the database come this far apart.</summary>
    public static readonly TimeSpan BackstopInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits for <paramref name="signal"/> (see <see cref="ResultSignals"/>), asking <paramref name="check"/>
    /// only as a backstop: after <see cref="BackstopFirstCheck"/>, then every <see cref="BackstopInterval"/>.
    /// A signal that is cancelled leaves the backstop to carry on alone.
    /// </summary>
    /// <param name="time">The clock to measure and sleep by; the system's when null.</param>
    /// <returns>What the signal or the check brought; null when the time is up or the caller left.</returns>
    public static async Task<T> UntilAsync<T>(Task<T> signal, Func<Task<T>> check, int seconds,
        CancellationToken cancellationToken, TimeSpan? firstCheck = null, TimeSpan? interval = null,
        TimeProvider time = null) where T : class
    {
        time ??= TimeProvider.System;
        var deadline = TimeSpan.FromSeconds(seconds);
        var started = time.GetTimestamp();
        TimeSpan Elapsed() => time.GetElapsedTime(started);
        var nextCheck = firstCheck ?? BackstopFirstCheck;

        while (Elapsed() < deadline && !cancellationToken.IsCancellationRequested)
        {
            // The clock can pass the next check during the loop (a signal that ends late, a busy
            // machine), and Task.Delay throws on a negative wait: the check is due now.
            var untilCheck = nextCheck - Elapsed();
            var remaining = deadline - Elapsed();
            using var sleeping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sleep = Task.Delay(NotNegative(untilCheck < remaining ? untilCheck : remaining), time, sleeping.Token);
            var woke = signal == null ? sleep : await Task.WhenAny(signal, sleep);

            if (woke == signal)
            {
                sleeping.Cancel();
                if (signal.IsCompletedSuccessfully && signal.Result != null) return signal.Result;
                signal = null;
                continue;
            }

            if (sleep.IsCanceled || Elapsed() < nextCheck) continue;
            if (await check() is { } found) return found;
            nextCheck = Elapsed() + (interval ?? BackstopInterval);
        }

        return null;
    }

    private static TimeSpan NotNegative(TimeSpan wait) => wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
}
