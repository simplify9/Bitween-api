using System;
using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// How many exchanges started in one slice of time with one status combination, as the summary's
/// grouping query returns it: the date split into parts, because the parts are what every database
/// provider can group on.
/// </summary>
/// <remarks>
/// The parts finer than the grouping asked for are left 0 - a by-day grouping has no hour.
/// </remarks>
public sealed class XchangeStartedCount
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public int Hour { get; set; }
    public int Minute { get; set; }

    /// <summary>The result's success; <c>null</c> when there is no result yet.</summary>
    public bool? Status { get; set; }

    public bool? ResponseBad { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// The pure half of the summary's histogram: picking a bucket size for a range, and folding the
/// grouped counts into contiguous, zero-filled buckets. No database here, so it can be tested on
/// its own.
/// </summary>
public static class XchangeHistogram
{
    /// <summary>
    /// The bucket sizes a histogram may use, in minutes, smallest first. Each divides a day or is a
    /// whole number of days, so buckets line up with the clock: 5-minute buckets start on :00, :05,
    /// and so on, never at whatever minute the range happened to start.
    /// </summary>
    public static readonly IReadOnlyList<int> BucketSizes =
        [1, 5, 10, 15, 30, 60, 120, 180, 360, 720, 1440, 10080];

    /// <summary>The most buckets a range is divided into, unless even the largest size gives more.</summary>
    public const int MaxBuckets = 60;

    /// <summary>
    /// The smallest bucket size that divides the range into at most <see cref="MaxBuckets"/>
    /// buckets. A day comes out as 49 half-hours, a week as 57 three-hour buckets, thirty days as 31
    /// days. Fewer is possible when the next size down was only just too fine: the last hour is 61
    /// one-minute buckets once both of its ends are counted, so it gets 13 five-minute ones instead.
    /// </summary>
    /// <remarks>
    /// A range longer than sixty weeks gets weekly buckets anyway, more than sixty of them: a week
    /// is the coarsest size there is, and inventing a longer one would no longer line up with
    /// anything on a calendar.
    /// </remarks>
    public static int ChooseBucketMinutes(DateTime start, DateTime end)
    {
        foreach (var size in BucketSizes)
            if (BucketCount(start, end, size) <= MaxBuckets)
                return size;

        return BucketSizes[^1];
    }

    /// <summary>
    /// How many buckets of <paramref name="bucketMinutes"/> cover the range, counting both the one
    /// <paramref name="start"/> falls in and the one <paramref name="end"/> falls in - the end is
    /// inclusive, as the list's "to" date is.
    /// </summary>
    public static long BucketCount(DateTime start, DateTime end, int bucketMinutes)
    {
        if (end < start)
            return 0;

        var ticks = BucketTicks(bucketMinutes);
        return (Floor(end, bucketMinutes).Ticks - Floor(start, bucketMinutes).Ticks) / ticks + 1;
    }

    /// <summary>
    /// The start of the bucket <paramref name="time"/> falls in, in UTC.
    /// </summary>
    /// <remarks>
    /// Counted from <see cref="DateTime.MinValue"/>, which is midnight, so every size that divides a
    /// day floors to a clock boundary; and a Monday, so weekly buckets start on Mondays.
    /// </remarks>
    public static DateTime Floor(DateTime time, int bucketMinutes)
    {
        var ticks = BucketTicks(bucketMinutes);
        return new DateTime(time.Ticks - time.Ticks % ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Folds the grouped counts into buckets of <paramref name="bucketMinutes"/>, from the one
    /// <paramref name="start"/> falls in to the one <paramref name="end"/> falls in, every bucket
    /// present whether anything started in it or not - a gap in the list would read as a gap in
    /// time to anything drawing it.
    /// </summary>
    /// <remarks>
    /// The status is read from the two columns the way <see cref="XchangeFilters.WhereStatus"/>
    /// reads it, so the bars add up to the counts beside them: no result is still running (the
    /// result's success is never null, so a null here means the row is missing), a success with a
    /// bad response is its own status, and any other success is a success. A count that falls
    /// outside the range is dropped rather than stretched onto the nearest bucket.
    /// </remarks>
    public static List<XchangeSummaryBucket> Fold(DateTime start, DateTime end, int bucketMinutes,
        IEnumerable<XchangeStartedCount> counts)
    {
        var buckets = new List<XchangeSummaryBucket>();
        if (end < start)
            return buckets;

        var first = Floor(start, bucketMinutes);
        var count = BucketCount(start, end, bucketMinutes);
        var size = TimeSpan.FromMinutes(bucketMinutes);
        for (var i = 0; i < count; i++)
            buckets.Add(new XchangeSummaryBucket { Start = first + size * i });

        var ticks = BucketTicks(bucketMinutes);
        foreach (var c in counts)
        {
            var started = new DateTime(c.Year, c.Month, c.Day, c.Hour, c.Minute, 0, DateTimeKind.Utc);
            var index = (Floor(started, bucketMinutes).Ticks - first.Ticks) / ticks;
            if (index < 0 || index >= buckets.Count)
                continue;

            var bucket = buckets[(int)index];
            if (c.Status == null)
                bucket.Processing += c.Count;
            else if (c.Status == false)
                bucket.Failed += c.Count;
            else if (c.ResponseBad == true)
                bucket.BadResponse += c.Count;
            else
                bucket.Success += c.Count;
        }

        return buckets;
    }

    private static long BucketTicks(int bucketMinutes) => bucketMinutes * TimeSpan.TicksPerMinute;
}
