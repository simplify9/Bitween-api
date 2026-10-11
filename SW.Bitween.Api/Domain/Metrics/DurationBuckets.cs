using System;

namespace SW.Bitween.Domain.Metrics;

/// <summary>
/// Durations counted into ten fixed buckets rather than kept one by one, so an hour of a million
/// exchanges is still ten numbers, buckets from many hours and nodes add up, and a percentile is
/// read off the counts. The answer is the bucket's upper edge: "p95 under 2.5 s", which is as exact
/// as a dashboard needs.
/// </summary>
public static class DurationBuckets
{
    /// <summary>Upper edges in milliseconds; the last bucket has no edge.</summary>
    public static readonly int[] UpperMs = [50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000, 30_000];

    public const int Count = 10;

    public static int IndexOf(long ms)
    {
        for (var i = 0; i < UpperMs.Length; i++)
            if (ms < UpperMs[i]) return i;
        return Count - 1;
    }

    /// <summary>
    /// The duration under which <paramref name="fraction"/> of the counted ones fell (0.95 for p95),
    /// or null when nothing was counted. Past the last edge, the longest one seen.
    /// </summary>
    public static long? Percentile(long[] buckets, double fraction, long maxMs)
    {
        long total = 0;
        foreach (var b in buckets) total += b;
        if (total == 0) return null;
        var target = (long)Math.Ceiling(total * fraction);
        long seen = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            seen += buckets[i];
            if (seen >= target) return i < UpperMs.Length ? Math.Min(UpperMs[i], Math.Max(maxMs, 1)) : maxMs;
        }
        return maxMs;
    }
}

/// <summary>The ten bucket counts, as columns, on every rollup that times something.</summary>
public abstract class TimedStats
{
    public long DurationSumMs { get; set; }
    public long DurationMaxMs { get; set; }
    public int D0 { get; set; }
    public int D1 { get; set; }
    public int D2 { get; set; }
    public int D3 { get; set; }
    public int D4 { get; set; }
    public int D5 { get; set; }
    public int D6 { get; set; }
    public int D7 { get; set; }
    public int D8 { get; set; }
    public int D9 { get; set; }

    public long[] Buckets() => [D0, D1, D2, D3, D4, D5, D6, D7, D8, D9];

    public void AddDuration(long ms, int times = 1)
    {
        DurationSumMs += ms * times;
        if (ms > DurationMaxMs) DurationMaxMs = ms;
        switch (DurationBuckets.IndexOf(ms))
        {
            case 0: D0 += times; break;
            case 1: D1 += times; break;
            case 2: D2 += times; break;
            case 3: D3 += times; break;
            case 4: D4 += times; break;
            case 5: D5 += times; break;
            case 6: D6 += times; break;
            case 7: D7 += times; break;
            case 8: D8 += times; break;
            default: D9 += times; break;
        }
    }

    public void AddTimes(TimedStats other)
    {
        DurationSumMs += other.DurationSumMs;
        if (other.DurationMaxMs > DurationMaxMs) DurationMaxMs = other.DurationMaxMs;
        D0 += other.D0; D1 += other.D1; D2 += other.D2; D3 += other.D3; D4 += other.D4;
        D5 += other.D5; D6 += other.D6; D7 += other.D7; D8 += other.D8; D9 += other.D9;
    }
}
