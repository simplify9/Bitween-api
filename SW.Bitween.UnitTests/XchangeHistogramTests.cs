using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Resources.Xchanges;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The exchange summary's histogram without a database: which bucket size a range gets, and how
/// grouped counts fold into contiguous, zero-filled buckets.
/// </summary>
[TestClass]
public class XchangeHistogramTests
{
    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    [TestMethod]
    public void A_range_gets_the_smallest_bucket_that_keeps_it_to_sixty()
    {
        var start = Utc(2026, 10, 11, 9);

        // Both ends count, so 59 minutes is 60 one-minute buckets and an hour is one too many.
        Assert.AreEqual(1, XchangeHistogram.ChooseBucketMinutes(start, start.AddMinutes(59)));
        Assert.AreEqual(5, XchangeHistogram.ChooseBucketMinutes(start, start.AddHours(1)));
        Assert.AreEqual(30, XchangeHistogram.ChooseBucketMinutes(start, start.AddDays(1)));
        Assert.AreEqual(180, XchangeHistogram.ChooseBucketMinutes(start, start.AddDays(7)));
        Assert.AreEqual(1440, XchangeHistogram.ChooseBucketMinutes(start, start.AddDays(30)));
        Assert.AreEqual(10080, XchangeHistogram.ChooseBucketMinutes(start, start.AddDays(180)));

        // Past sixty weeks there is no larger size to move to, so weeks it stays.
        Assert.AreEqual(10080, XchangeHistogram.ChooseBucketMinutes(start, start.AddYears(5)));
    }

    [TestMethod]
    public void Every_chosen_size_gives_at_most_sixty_buckets_within_reach()
    {
        var start = Utc(2026, 1, 1, 0, 7, 13);
        foreach (var minutes in new[] { 0, 1, 7, 59, 60, 61, 119, 300, 1439, 1441, 4000, 10079, 40000, 200000 })
        {
            var end = start.AddMinutes(minutes);
            var size = XchangeHistogram.ChooseBucketMinutes(start, end);
            Assert.IsTrue(XchangeHistogram.BucketCount(start, end, size) <= XchangeHistogram.MaxBuckets,
                $"{minutes} minutes in {size}-minute buckets");

            // And it is the smallest that does.
            var smaller = XchangeHistogram.BucketSizes.TakeWhile(s => s < size);
            Assert.IsTrue(smaller.All(s => XchangeHistogram.BucketCount(start, end, s) > XchangeHistogram.MaxBuckets));
        }
    }

    [TestMethod]
    public void Buckets_line_up_with_the_clock_not_the_range()
    {
        var time = Utc(2026, 10, 15, 13, 47, 31);

        Assert.AreEqual(Utc(2026, 10, 15, 13, 47), XchangeHistogram.Floor(time, 1));
        Assert.AreEqual(Utc(2026, 10, 15, 13, 45), XchangeHistogram.Floor(time, 15));
        Assert.AreEqual(Utc(2026, 10, 15, 12), XchangeHistogram.Floor(time, 180));
        Assert.AreEqual(Utc(2026, 10, 15, 12), XchangeHistogram.Floor(time, 720));
        Assert.AreEqual(Utc(2026, 10, 15), XchangeHistogram.Floor(time, 1440));
        // 15 October 2026 is a Thursday; weeks start on Monday the 12th.
        Assert.AreEqual(Utc(2026, 10, 12), XchangeHistogram.Floor(time, 10080));
        Assert.AreEqual(DateTimeKind.Utc, XchangeHistogram.Floor(time, 60).Kind);
    }

    [TestMethod]
    public void Folding_fills_every_bucket_and_sorts_each_count_by_status()
    {
        var start = Utc(2026, 10, 11, 10, 7);
        var end = Utc(2026, 10, 11, 12);

        var buckets = XchangeHistogram.Fold(start, end, 5,
        [
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 10, Minute = 8, Status = false, Count = 2 },
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 10, Minute = 9, Status = true, ResponseBad = false, Count = 3 },
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 10, Minute = 9, Status = true, Count = 1 },
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 11, Minute = 31, Status = null, Count = 4 },
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 12, Minute = 0, Status = true, ResponseBad = true, Count = 5 },
            // Outside the range on both sides: dropped, not pulled onto the nearest bucket.
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 9, Minute = 59, Status = false, Count = 100 },
            new XchangeStartedCount { Year = 2026, Month = 10, Day = 11, Hour = 12, Minute = 5, Status = false, Count = 100 },
        ]);

        Assert.AreEqual(24, buckets.Count);
        Assert.AreEqual(Utc(2026, 10, 11, 10, 5), buckets[0].Start);
        Assert.AreEqual(Utc(2026, 10, 11, 12), buckets[^1].Start);
        for (var i = 1; i < buckets.Count; i++)
            Assert.AreEqual(TimeSpan.FromMinutes(5), buckets[i].Start - buckets[i - 1].Start);

        Assert.AreEqual(2, buckets[0].Failed);
        Assert.AreEqual(4, buckets[0].Success);
        Assert.AreEqual(4, buckets.Single(b => b.Start == Utc(2026, 10, 11, 11, 30)).Processing);
        Assert.AreEqual(5, buckets[^1].BadResponse);
        Assert.AreEqual(15, buckets.Sum(b => b.Processing + b.Success + b.BadResponse + b.Failed));

        // The empty ones are there, at zero.
        Assert.AreEqual(21, buckets.Count(b => b.Processing + b.Success + b.BadResponse + b.Failed == 0));
    }

    [TestMethod]
    public void Day_level_counts_fold_into_weeks()
    {
        var start = Utc(2026, 1, 1);
        var end = Utc(2027, 3, 1);
        var size = XchangeHistogram.ChooseBucketMinutes(start, end);
        Assert.AreEqual(10080, size);

        var buckets = XchangeHistogram.Fold(start, end, size,
        [
            new XchangeStartedCount { Year = 2026, Month = 1, Day = 1, Status = false, Count = 1 },
            new XchangeStartedCount { Year = 2026, Month = 1, Day = 4, Status = false, Count = 1 },
            new XchangeStartedCount { Year = 2026, Month = 1, Day = 5, Status = false, Count = 1 },
        ]);

        // 1 January 2026 is a Thursday: its week began Monday 29 December, and runs to Sunday the 4th.
        Assert.AreEqual(Utc(2025, 12, 29), buckets[0].Start);
        Assert.AreEqual(2, buckets[0].Failed);
        Assert.AreEqual(1, buckets[1].Failed);
    }

    [TestMethod]
    public void A_range_that_ends_before_it_starts_has_no_buckets()
    {
        var start = Utc(2026, 10, 11, 12);
        Assert.AreEqual(0, XchangeHistogram.BucketCount(start, start.AddMinutes(-1), 5));
        Assert.AreEqual(0, XchangeHistogram.Fold(start, start.AddMinutes(-1), 5, []).Count);

        // A single instant is one bucket.
        Assert.AreEqual(1, XchangeHistogram.Fold(start, start, 1, []).Count);
    }
}
