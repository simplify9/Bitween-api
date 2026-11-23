using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.XchangeStates;

namespace SW.Bitween.UnitTests;

/// <summary>The exchange status fill's progress, by the date range it has covered.</summary>
[TestClass]
public class XchangeStateProgressTests
{
    private static readonly DateTime Began = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Halfway_through_the_range_after_an_hour_finishes_an_hour_later()
    {
        var (percent, finish) = XchangeStateProgress.Estimate(Began, Began.AddDays(-5), Began.AddDays(-10),
            Began.AddHours(1));
        Assert.AreEqual(50, percent);
        Assert.AreEqual(Began.AddHours(2), finish);
    }

    [TestMethod]
    public void A_fill_stalled_for_days_near_its_start_gives_no_date_rather_than_one_past_any()
    {
        var (percent, finish) = XchangeStateProgress.Estimate(Began, Began.AddMinutes(-1), Began.AddYears(-3),
            Began.AddDays(2));
        Assert.AreEqual(0, percent);
        Assert.IsNull(finish);
    }
}
