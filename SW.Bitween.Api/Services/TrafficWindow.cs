using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// A subscription's <see cref="ExpectTraffic"/>, checked and ready to ask: is an exchange due now?
/// <para>
/// The window is a set of local days, each open from <c>From</c> to <c>To</c> (the whole day when
/// neither is set) in the expectation's time zone. Silence is only counted inside it: a quiet stretch
/// has to lie wholly in one open stretch of the window, so a partner that sends nothing overnight is
/// not reported at 08:01 because its last order came in at 21:55.
/// </para>
/// </summary>
public sealed class TrafficWindow
{
    public const int MinimumMinutes = 5;

    // A week: past that, a whole-week window would never be found quiet anyway.
    public const int MaximumMinutes = 7 * 24 * 60;

    static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public ExpectTraffic Expectation { get; }
    public TimeSpan Every { get; }
    public TimeZoneInfo Zone { get; }
    readonly bool[] _days;
    readonly TimeSpan? _from, _to;

    TrafficWindow(ExpectTraffic expectation, bool[] days, TimeSpan? from, TimeSpan? to, TimeZoneInfo zone)
    {
        Expectation = expectation;
        Every = TimeSpan.FromMinutes(expectation.EveryMinutes);
        _days = days;
        _from = from;
        _to = to;
        Zone = zone;
    }

    /// <summary>
    /// Checks an expectation and returns it in its stored form: days in week order with their usual
    /// spelling, blanks as null. Null stays null. Throws <see cref="SWValidationException"/> with a
    /// message that says what to change.
    /// </summary>
    public static ExpectTraffic Normalize(ExpectTraffic expectation) =>
        expectation == null ? null : Parse(expectation).Expectation;

    /// <inheritdoc cref="Normalize"/>
    public static TrafficWindow Parse(ExpectTraffic expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);

        if (expectation.EveryMinutes < MinimumMinutes || expectation.EveryMinutes > MaximumMinutes)
            throw Invalid($"Expect an exchange every {MinimumMinutes} to {MaximumMinutes} minutes; " +
                          $"{expectation.EveryMinutes} is outside that. Bitween checks every few minutes, so a shorter gap can't be told apart.");

        var days = new bool[7];
        foreach (var day in (expectation.Days ?? []).Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var index = Array.FindIndex(DayNames, n => string.Equals(n, day.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw Invalid($"'{day}' isn't a day. Use Sun, Mon, Tue, Wed, Thu, Fri or Sat.");
            days[index] = true;
        }
        // None named means every day.
        var everyDay = !days.Any(d => d);
        if (everyDay) Array.Fill(days, true);

        var from = Time(expectation.From, "From");
        var to = Time(expectation.To, "To");
        if (from.HasValue != to.HasValue)
            throw Invalid("Give both From and To, or neither for the whole day.");
        if (from.HasValue && to <= from)
            throw Invalid($"To ({expectation.To}) has to be later than From ({expectation.From}). " +
                          "A window can't run past midnight; leave the times out to watch whole days.");
        if (from.HasValue && expectation.EveryMinutes > (to.Value - from.Value).TotalMinutes)
            throw Invalid($"Every {expectation.EveryMinutes} minutes is longer than the {expectation.From}-{expectation.To} window, " +
                          "so the subscription could never be found quiet.");

        var zoneId = string.IsNullOrWhiteSpace(expectation.TimeZone) ? null : expectation.TimeZone.Trim();
        TimeZoneInfo zone;
        try
        {
            zone = zoneId == null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Invalid($"'{zoneId}' isn't a time zone Bitween knows. Use an IANA name such as Asia/Riyadh or Europe/London, or leave it empty for UTC.");
        }

        var normalized = new ExpectTraffic
        {
            EveryMinutes = expectation.EveryMinutes,
            Days = everyDay ? null : DayNames.Where((_, i) => days[i]).ToList(),
            From = from?.ToString(@"hh\:mm"),
            To = to?.ToString(@"hh\:mm"),
            TimeZone = zoneId,
        };
        return new TrafficWindow(normalized, days, from, to, zone);
    }

    static TimeSpan? Time(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeSpan.TryParseExact(value.Trim(), [@"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out var time) &&
            time < TimeSpan.FromDays(1))
            return time;
        throw Invalid($"{field} '{value}' isn't a time of day. Use HH:mm, such as 08:00 or 22:30.");
    }

    static SWValidationException Invalid(string message) => new("ExpectTraffic", message);

    /// <summary>
    /// When the open stretch of the window that <paramref name="utcNow"/> is in began, in UTC; null
    /// when the window is closed. Consecutive whole days are one stretch, so Sunday's silence carries
    /// into Monday; a window with times opens afresh each day.
    /// </summary>
    public DateTime? OpenedAt(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone);
        if (!_days[(int)local.DayOfWeek]) return null;

        if (_from.HasValue)
        {
            var time = local.TimeOfDay;
            if (time < _from.Value || time >= _to!.Value) return null;
            // Measured back from now rather than converted from local time, which a clock change
            // can make ambiguous or skip altogether.
            return utcNow - (time - _from.Value);
        }

        var opened = utcNow - local.TimeOfDay;
        for (var back = 1; back <= 7; back++)
        {
            if (!_days[((int)local.DayOfWeek - back + 7) % 7]) return opened;
            opened -= TimeSpan.FromDays(1);
        }
        // Every day: the window never closes.
        return DateTime.MinValue;
    }

    /// <summary>
    /// The moment an exchange must have started after for the subscription to count as not quiet at
    /// <paramref name="utcNow"/>; null when it can't be quiet yet, because the window is closed or has
    /// not been open for a whole <see cref="Every"/>.
    /// </summary>
    public DateTime? QuietUnlessAfter(DateTime utcNow)
    {
        var opened = OpenedAt(utcNow);
        if (opened == null) return null;
        var cutoff = utcNow - Every;
        return opened <= cutoff ? cutoff : null;
    }

    /// <summary>"every 15 minutes, Sun-Thu 08:00-22:00 Asia/Riyadh": the expectation in words.</summary>
    public string Describe()
    {
        var days = DescribeDays();
        var hours = _from.HasValue ? $" {Expectation.From}-{Expectation.To}" : "";
        return $"every {Expectation.EveryMinutes} minutes, {days}{hours} {Expectation.TimeZone ?? "UTC"}";
    }

    string DescribeDays()
    {
        if (_days.All(d => d)) return "every day";
        // Runs of three or more read as a range, the rest as a list, both in week order.
        var parts = new List<string>();
        for (var i = 0; i < 7;)
        {
            if (!_days[i]) { i++; continue; }
            var end = i;
            while (end + 1 < 7 && _days[end + 1]) end++;
            parts.Add(end - i >= 2 ? $"{DayNames[i]}-{DayNames[end]}" : string.Join(", ", DayNames[i..(end + 1)]));
            i = end + 1;
        }
        return string.Join(", ", parts);
    }
}
