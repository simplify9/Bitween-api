using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Metrics;

namespace SW.Bitween.Services.Metrics;

/// <summary>One subscription's health over a window, as the lists and the dashboard show it.</summary>
public class SubscriptionHealthRow
{
    public int SubscriptionId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    /// <summary>healthy, degraded, failing, silent, idle, paused, inactive.</summary>
    public string Status { get; set; }
    public ExchangeTotals Totals { get; set; }
    public double? SuccessRate => Totals.SuccessRate;
    public long? P95Ms => Totals.P95Ms;
    /// <summary>The hour of its latest exchange in the last 30 days.</summary>
    public DateTime? LastExchangeHour { get; set; }
    /// <summary>For a silent one: how long it has been quiet, and what its usual gap is.</summary>
    public double? QuietHours { get; set; }
    public double? UsualGapHours { get; set; }
    /// <summary>Exchanges per day over the window, oldest first.</summary>
    public long[] Daily { get; set; }
}

/// <summary>
/// Whether each subscription is doing what it usually does. Most of it is plain rates; the part
/// that can't be had elsewhere is "silent": a subscription that normally has traffic and has gone
/// quiet, which nothing fails on and so nothing reported. Usual is read from the last week: a
/// subscription with traffic in many of its hours is expected again soon, one with traffic once a
/// day is not missed for a day.
/// </summary>
public static class SubscriptionHealth
{
    /// <summary>Hours with traffic in the last week before a subscription is regular enough to be missed.</summary>
    public const int RegularAfterActiveHours = 6;
    public const double SilentAfterAtLeastHours = 6;
    public const double SilentAfterGaps = 3;

    public static async Task<List<SubscriptionHealthRow>> ComputeAsync(BitweenDbContext db, int days, DateTime now)
    {
        var to = MetricsRecorder.HourOf(now).AddHours(1);
        var from = to.AddDays(-days);
        var week = to.AddDays(-7);
        var month = to.AddDays(-30);

        var subscriptions = await db.Set<Subscription>().AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Type, s.Inactive, s.PausedOn })
            .ToListAsync();

        var totals = (await MetricsQueries.Exchanges(db, from, to).GroupBy(s => s.SubscriptionId).SumBy())
            .ToDictionary(t => t.Key);
        var activeHours = await MetricsQueries.Exchanges(db, week, to)
            .Where(s => s.Total > 0)
            .GroupBy(s => s.SubscriptionId)
            .Select(g => new { g.Key, Hours = g.Select(x => x.Hour).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Hours);
        var last = await MetricsQueries.Exchanges(db, month, to)
            .Where(s => s.Total > 0)
            .GroupBy(s => s.SubscriptionId)
            .Select(g => new { g.Key, Last = g.Max(x => x.Hour) })
            .ToDictionaryAsync(x => x.Key, x => MetricsQueries.Utc(x.Last));
        var dailyRows = await MetricsQueries.Exchanges(db, from, to)
            .GroupBy(s => new { s.SubscriptionId, Day = s.Hour.Date })
            .Select(g => new { g.Key.SubscriptionId, g.Key.Day, Total = g.Sum(x => (long)x.Total) })
            .ToListAsync();
        var firstDay = from.Date;
        var daily = dailyRows.GroupBy(r => r.SubscriptionId).ToDictionary(g => g.Key, g =>
        {
            var series = new long[days];
            foreach (var r in g)
            {
                var i = (int)(r.Day.Date - firstDay).TotalDays;
                if (i >= 0 && i < days) series[i] += r.Total;
            }
            return series;
        });

        return subscriptions.Select(s =>
        {
            var t = totals.TryGetValue(s.Id, out var found) ? found : new ExchangeTotals<int> { Key = s.Id };
            var row = new SubscriptionHealthRow
            {
                SubscriptionId = s.Id,
                Name = s.Name,
                Type = s.Type.ToString(),
                Totals = t,
                LastExchangeHour = last.TryGetValue(s.Id, out var l) ? l : null,
                Daily = daily.TryGetValue(s.Id, out var d) ? d : new long[days],
            };
            row.Status = Judge(row, s.Inactive, s.PausedOn != null, activeHours.GetValueOrDefault(s.Id), now);
            return row;
        }).ToList();
    }

    static string Judge(SubscriptionHealthRow row, bool inactive, bool paused, int activeHoursLastWeek, DateTime now)
    {
        if (inactive) return "inactive";
        if (paused) return "paused";

        if (row.LastExchangeHour != null && activeHoursLastWeek >= RegularAfterActiveHours)
        {
            var quiet = (now - row.LastExchangeHour.Value.AddHours(1)).TotalHours;
            var usualGap = 168.0 / activeHoursLastWeek;
            if (quiet > Math.Max(SilentAfterAtLeastHours, SilentAfterGaps * usualGap))
            {
                row.QuietHours = Math.Round(quiet, 1);
                row.UsualGapHours = Math.Round(usualGap, 1);
                return "silent";
            }
        }

        if (row.Totals.Total == 0) return "idle";
        var rate = row.Totals.SuccessRate ?? 100;
        return rate < 90 ? "failing" : rate < 99 ? "degraded" : "healthy";
    }
}
