using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween;

/// <summary>
/// Holds a synchronous gateway call open while its exchange is processed, polling for the result.
/// </summary>
/// <remarks>
/// The loops this replaced compared the polling step — capped at eight seconds — with the requested
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
                await Task.Delay(delay, cancellationToken);
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
}
