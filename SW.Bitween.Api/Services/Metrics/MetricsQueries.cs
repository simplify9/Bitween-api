using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain.Metrics;

namespace SW.Bitween.Services.Metrics;

/// <summary>Exchange counts added up over some rows, with the rates and percentiles read off them.</summary>
public class ExchangeTotals
{
    public long Total { get; set; }
    public long Succeeded { get; set; }
    public long BadResponse { get; set; }
    public long Failed { get; set; }
    public long FailedAtMapper { get; set; }
    public long FailedAtHandler { get; set; }
    public long Retries { get; set; }
    public long ManualRetries { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationMaxMs { get; set; }
    public long D0 { get; set; }
    public long D1 { get; set; }
    public long D2 { get; set; }
    public long D3 { get; set; }
    public long D4 { get; set; }
    public long D5 { get; set; }
    public long D6 { get; set; }
    public long D7 { get; set; }
    public long D8 { get; set; }
    public long D9 { get; set; }

    public long[] Buckets() => [D0, D1, D2, D3, D4, D5, D6, D7, D8, D9];

    /// <summary>Of the finished exchanges, the share that succeeded (a bad response is not a success). Null without any.</summary>
    public double? SuccessRate => Total == 0 ? null : Math.Round(100.0 * Succeeded / Total, 2);
    public long? P50Ms => DurationBuckets.Percentile(Buckets(), 0.5, DurationMaxMs);
    public long? P95Ms => DurationBuckets.Percentile(Buckets(), 0.95, DurationMaxMs);
    public long? AverageMs => Total == 0 ? null : DurationSumMs / Total;

    public void Add(ExchangeTotals o)
    {
        Total += o.Total; Succeeded += o.Succeeded; BadResponse += o.BadResponse; Failed += o.Failed;
        FailedAtMapper += o.FailedAtMapper; FailedAtHandler += o.FailedAtHandler; Retries += o.Retries;
        ManualRetries += o.ManualRetries; BytesIn += o.BytesIn; BytesOut += o.BytesOut; DurationSumMs += o.DurationSumMs;
        DurationMaxMs = Math.Max(DurationMaxMs, o.DurationMaxMs);
        D0 += o.D0; D1 += o.D1; D2 += o.D2; D3 += o.D3; D4 += o.D4; D5 += o.D5; D6 += o.D6; D7 += o.D7; D8 += o.D8; D9 += o.D9;
    }
}

public class ExchangeTotals<TKey> : ExchangeTotals
{
    public TKey Key { get; set; }
}

/// <summary>Adapter call counts added up, with rates and percentiles.</summary>
public class AdapterTotals
{
    public long Calls { get; set; }
    public long Failures { get; set; }
    public long Timeouts { get; set; }
    public long LimitStops { get; set; }
    public long Crashes { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationMaxMs { get; set; }
    public long D0 { get; set; }
    public long D1 { get; set; }
    public long D2 { get; set; }
    public long D3 { get; set; }
    public long D4 { get; set; }
    public long D5 { get; set; }
    public long D6 { get; set; }
    public long D7 { get; set; }
    public long D8 { get; set; }
    public long D9 { get; set; }

    public long[] Buckets() => [D0, D1, D2, D3, D4, D5, D6, D7, D8, D9];
    public double? FailureRate => Calls == 0 ? null : Math.Round(100.0 * Failures / Calls, 2);
    public long? P50Ms => DurationBuckets.Percentile(Buckets(), 0.5, DurationMaxMs);
    public long? P95Ms => DurationBuckets.Percentile(Buckets(), 0.95, DurationMaxMs);
}

public class AdapterTotals<TKey> : AdapterTotals
{
    public TKey Key { get; set; }
}

/// <summary>
/// The grouped sums every metrics page is made of, computed by the database over the hourly rows.
/// Sums are taken as 64-bit so a busy year can't overflow a 32-bit column total.
/// </summary>
public static class MetricsQueries
{
    /// <summary>Rows of real counts: without the markers the history backfill keeps.</summary>
    public static IQueryable<ExchangeStats> Exchanges(BitweenDbContext db, DateTime from, DateTime to) =>
        db.Set<ExchangeStats>().AsNoTracking().Where(s => s.Hour >= from && s.Hour < to && s.SubscriptionId >= 0);

    public static IQueryable<AdapterStats> Adapters(BitweenDbContext db, DateTime from, DateTime to) =>
        db.Set<AdapterStats>().AsNoTracking().Where(s => s.Hour >= from && s.Hour < to);

    public static Task<List<ExchangeTotals<TKey>>> SumBy<TKey>(this IQueryable<IGrouping<TKey, ExchangeStats>> groups) =>
        groups.Select(g => new ExchangeTotals<TKey>
        {
            Key = g.Key,
            Total = g.Sum(x => (long)x.Total),
            Succeeded = g.Sum(x => (long)x.Succeeded),
            BadResponse = g.Sum(x => (long)x.BadResponse),
            Failed = g.Sum(x => (long)x.Failed),
            FailedAtMapper = g.Sum(x => (long)x.FailedAtMapper),
            FailedAtHandler = g.Sum(x => (long)x.FailedAtHandler),
            Retries = g.Sum(x => (long)x.Retries),
            ManualRetries = g.Sum(x => (long)x.ManualRetries),
            BytesIn = g.Sum(x => x.BytesIn),
            BytesOut = g.Sum(x => x.BytesOut),
            DurationSumMs = g.Sum(x => x.DurationSumMs),
            DurationMaxMs = g.Max(x => x.DurationMaxMs),
            D0 = g.Sum(x => (long)x.D0), D1 = g.Sum(x => (long)x.D1), D2 = g.Sum(x => (long)x.D2),
            D3 = g.Sum(x => (long)x.D3), D4 = g.Sum(x => (long)x.D4), D5 = g.Sum(x => (long)x.D5),
            D6 = g.Sum(x => (long)x.D6), D7 = g.Sum(x => (long)x.D7), D8 = g.Sum(x => (long)x.D8),
            D9 = g.Sum(x => (long)x.D9),
        }).ToListAsync();

    public static Task<List<AdapterTotals<TKey>>> SumBy<TKey>(this IQueryable<IGrouping<TKey, AdapterStats>> groups) =>
        groups.Select(g => new AdapterTotals<TKey>
        {
            Key = g.Key,
            Calls = g.Sum(x => (long)x.Calls),
            Failures = g.Sum(x => (long)x.Failures),
            Timeouts = g.Sum(x => (long)x.Timeouts),
            LimitStops = g.Sum(x => (long)x.LimitStops),
            Crashes = g.Sum(x => (long)x.Crashes),
            DurationSumMs = g.Sum(x => x.DurationSumMs),
            DurationMaxMs = g.Max(x => x.DurationMaxMs),
            D0 = g.Sum(x => (long)x.D0), D1 = g.Sum(x => (long)x.D1), D2 = g.Sum(x => (long)x.D2),
            D3 = g.Sum(x => (long)x.D3), D4 = g.Sum(x => (long)x.D4), D5 = g.Sum(x => (long)x.D5),
            D6 = g.Sum(x => (long)x.D6), D7 = g.Sum(x => (long)x.D7), D8 = g.Sum(x => (long)x.D8),
            D9 = g.Sum(x => (long)x.D9),
        }).ToListAsync();

    public static int Days(int? days) => Math.Clamp(days ?? 7, 1, 90);

    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>A minute is as fresh as the counts themselves: they are added once a minute.</summary>
    public static Task<T> Cached<T>(this IMemoryCache cache, string key, Func<Task<T>> compute) =>
        cache.GetOrCreateAsync($"bitween.metrics.{key}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return compute();
        })!;

    /// <summary>
    /// When the metrics begin: the oldest hour folded in from history, or failing that the hour live
    /// recording started. Pages say "since" this rather than show an empty past as zero traffic.
    /// </summary>
    public static async Task<DateTime?> SinceAsync(BitweenDbContext db)
    {
        var oldest = await db.Set<ExchangeStats>().AsNoTracking()
            .OrderBy(s => s.Hour).Select(s => (DateTime?)s.Hour).FirstOrDefaultAsync();
        return oldest == null ? null : Utc(oldest.Value);
    }

    public static async Task<DateTime?> AdapterSinceAsync(BitweenDbContext db)
    {
        var oldest = await db.Set<AdapterStats>().AsNoTracking()
            .OrderBy(s => s.Hour).Select(s => (DateTime?)s.Hour).FirstOrDefaultAsync();
        return oldest == null ? null : Utc(oldest.Value);
    }
}
