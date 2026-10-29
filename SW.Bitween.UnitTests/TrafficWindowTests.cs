using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// When a subscription that expects traffic counts as quiet. 2026-10-11 is a Sunday; Asia/Riyadh is
/// UTC+3 all year.
/// </summary>
[TestClass]
public class TrafficWindowTests
{
    static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    static TrafficWindow Riyadh(int every = 15, string[] days = null, string from = "08:00", string to = "22:00") =>
        TrafficWindow.Parse(new ExpectTraffic
        {
            EveryMinutes = every,
            Days = days ?? ["Sun", "Mon", "Tue", "Wed", "Thu"],
            From = from,
            To = to,
            TimeZone = "Asia/Riyadh",
        });

    [TestMethod]
    public void Inside_the_window_silence_is_counted_back_from_now()
    {
        // Monday 08:30 in Riyadh.
        Assert.AreEqual(Utc(12, 5, 15), Riyadh().QuietUnlessAfter(Utc(12, 5, 30)));
        Assert.AreEqual(Utc(12, 5, 0), Riyadh().OpenedAt(Utc(12, 5, 30)));
    }

    [TestMethod]
    public void A_window_that_just_opened_waits_a_whole_gap_before_it_can_be_quiet()
    {
        // 08:10: yesterday evening's last order is not a reason to raise ten minutes into the day.
        Assert.IsNull(Riyadh().QuietUnlessAfter(Utc(12, 5, 10)));
        Assert.AreEqual(Utc(12, 5, 0), Riyadh().QuietUnlessAfter(Utc(12, 5, 15)));
    }

    [TestMethod]
    public void Outside_the_hours_or_the_days_nothing_is_due()
    {
        Assert.IsNull(Riyadh().OpenedAt(Utc(12, 4, 59)), "07:59 local");
        Assert.IsNull(Riyadh().OpenedAt(Utc(12, 19, 0)), "22:00 local is closed: To is exclusive");
        Assert.IsNotNull(Riyadh().OpenedAt(Utc(12, 18, 59)), "21:59 local");
        Assert.IsNull(Riyadh().QuietUnlessAfter(Utc(16, 9, 0)), "Friday");
        Assert.IsNull(Riyadh().QuietUnlessAfter(Utc(10, 9, 0)), "Saturday");
    }

    [TestMethod]
    public void Days_and_times_are_read_in_the_time_zone()
    {
        // 20:00 UTC on Monday is 23:00 in Riyadh, closed there but open by UTC.
        Assert.IsNull(Riyadh().OpenedAt(Utc(12, 20, 0)));
        var utc = TrafficWindow.Parse(new ExpectTraffic { EveryMinutes = 15, From = "08:00", To = "22:00" });
        Assert.AreEqual(Utc(12, 8, 0), utc.OpenedAt(Utc(12, 20, 0)));

        // Sunday 22:00 UTC is already Monday in Riyadh; Monday 22:00 UTC is Tuesday 01:00.
        var allDayMonday = TrafficWindow.Parse(new ExpectTraffic { EveryMinutes = 15, Days = ["Mon"], TimeZone = "Asia/Riyadh" });
        Assert.IsNotNull(allDayMonday.OpenedAt(Utc(11, 22, 0)));
        Assert.IsNull(allDayMonday.OpenedAt(Utc(12, 22, 0)));
    }

    [TestMethod]
    public void Whole_days_in_a_row_carry_silence_over_midnight()
    {
        var days = TrafficWindow.Parse(new ExpectTraffic
            { EveryMinutes = 60, Days = ["Sun", "Mon", "Tue", "Wed", "Thu"], TimeZone = "Asia/Riyadh" });

        // Monday 00:10 local: open since Sunday 00:00 local, so an hour of silence spans midnight.
        Assert.AreEqual(Utc(10, 21, 0), days.OpenedAt(Utc(11, 21, 10)));
        Assert.AreEqual(Utc(11, 20, 10), days.QuietUnlessAfter(Utc(11, 21, 10)));

        // Sunday 00:30 local: Saturday was closed, so the window is only half an hour old.
        Assert.IsNull(days.QuietUnlessAfter(Utc(10, 21, 30)));

        // Friday 00:10 local: Thursday's window ended at midnight.
        Assert.IsNull(days.OpenedAt(Utc(15, 21, 10)));
    }

    [TestMethod]
    public void A_window_with_hours_opens_afresh_each_day()
    {
        // Tuesday 08:05 local: Monday's window closed at 22:00 and doesn't carry over.
        Assert.IsNull(Riyadh().QuietUnlessAfter(Utc(13, 5, 5)));
    }

    [TestMethod]
    public void Every_day_all_day_never_closes()
    {
        var always = TrafficWindow.Parse(new ExpectTraffic { EveryMinutes = 30 });
        Assert.AreEqual(DateTime.MinValue, always.OpenedAt(Utc(12, 0, 0)));
        Assert.AreEqual(Utc(11, 23, 30), always.QuietUnlessAfter(Utc(12, 0, 0)));
    }

    [TestMethod]
    public void A_zone_with_summer_time_follows_its_clock()
    {
        // London leaves summer time on 2026-10-25: 08:00 local is 07:00 UTC before, 08:00 after.
        var london = TrafficWindow.Parse(new ExpectTraffic
            { EveryMinutes = 15, From = "08:00", To = "17:00", TimeZone = "Europe/London" });
        Assert.AreEqual(new DateTime(2026, 10, 23, 7, 0, 0, DateTimeKind.Utc),
            london.OpenedAt(new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc)));
        Assert.AreEqual(new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc),
            london.OpenedAt(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc)));
    }

    [TestMethod]
    public void It_is_stored_in_one_spelling()
    {
        var stored = TrafficWindow.Normalize(new ExpectTraffic
        {
            EveryMinutes = 15, Days = ["thu", " Sun", "MON", "Mon"], From = "8:00", To = "22:00", TimeZone = " ",
        });
        CollectionAssert.AreEqual(new[] { "Sun", "Mon", "Thu" }, stored.Days.ToArray());
        Assert.AreEqual("08:00", stored.From);
        Assert.IsNull(stored.TimeZone);

        Assert.IsNull(TrafficWindow.Normalize(new ExpectTraffic { EveryMinutes = 15, Days = [] }).Days);
        Assert.IsNull(TrafficWindow.Normalize(null));
    }

    [TestMethod]
    public void It_reads_as_a_sentence()
    {
        Assert.AreEqual("every 15 minutes, Sun-Thu 08:00-22:00 Asia/Riyadh", Riyadh().Describe());
        Assert.AreEqual("every 30 minutes, Mon, Wed, Fri UTC",
            TrafficWindow.Parse(new ExpectTraffic { EveryMinutes = 30, Days = ["Fri", "Mon", "Wed"] }).Describe());
        Assert.AreEqual("every 30 minutes, every day UTC",
            TrafficWindow.Parse(new ExpectTraffic { EveryMinutes = 30 }).Describe());
    }

    [DataTestMethod]
    [DataRow(4, null, null, null, null, "5 to")]
    [DataRow(15, "Funday", null, null, null, "isn't a day")]
    [DataRow(15, null, "08:00", null, null, "both From and To")]
    [DataRow(15, null, "22:00", "08:00", null, "later than From")]
    [DataRow(15, null, "08:00", "08:00", null, "later than From")]
    [DataRow(15, null, "25:00", "26:00", null, "isn't a time of day")]
    [DataRow(15, null, "8am", "5pm", null, "isn't a time of day")]
    [DataRow(120, null, "08:00", "09:00", null, "longer than the 08:00-09:00 window")]
    [DataRow(15, null, null, null, "Mars/Olympus", "isn't a time zone")]
    public void A_bad_expectation_says_what_is_wrong(int every, string day, string from, string to, string zone, string says)
    {
        var e = Assert.ThrowsException<SWValidationException>(() => TrafficWindow.Parse(new ExpectTraffic
        {
            EveryMinutes = every, Days = day == null ? null : [day], From = from, To = to, TimeZone = zone,
        }));
        StringAssert.Contains(e.Message, says);
    }
}
