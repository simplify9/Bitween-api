using System;
using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Domain;

namespace SW.Bitween;

/// <summary>
/// Whether retention removes exchanges before an aggregation collects them. An aggregation only rolls up
/// exchanges still in the table, so one that waits longer between runs than exchanges are kept never sees
/// those removed in between. Nothing holds them back for it: the settings page and the aggregation say so.
/// </summary>
public static class AggregationRetention
{
    /// <summary>
    /// The longest wait between two runs of <paramref name="schedules"/> in the 100 days after
    /// <paramref name="from"/>: long enough to take in a 31-day month whatever the date. Null with no schedule.
    /// </summary>
    public static TimeSpan? LongestGap(IReadOnlyCollection<Schedule> schedules, DateTime from)
    {
        if (schedules.Count == 0) return null;

        var run = schedules.Min(s => s.Next(from));
        var longest = TimeSpan.Zero;
        for (var end = from.AddDays(100); run < end;)
        {
            var next = schedules.Min(s => s.Next(run.AddMinutes(1)));
            if (next - run > longest) longest = next - run;
            run = next;
        }

        return longest;
    }

    /// <summary>Whole days, rounded up, an aggregation can wait between runs when that's longer than exchanges are kept; otherwise null.</summary>
    public static int? MissedDays(IReadOnlyCollection<Schedule> schedules, int retentionDays, DateTime from) =>
        retentionDays > 0 && LongestGap(schedules, from) is { } gap && gap > TimeSpan.FromDays(retentionDays)
            ? (int)Math.Ceiling(gap.TotalDays)
            : null;
}
