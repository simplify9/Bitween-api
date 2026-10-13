using System.Globalization;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// An adapter saying when to try again: its message starts with <c>{{retryAfter:SECONDS}}</c>, which
/// Bitween reads to schedule the retry its policy allows no sooner than that.
/// </summary>
/// <remarks>
/// Carried as the inner exception of the error a built-in adapter has always thrown, so the error's
/// own text - which retry policies match, "503:" first - stays exactly as it was.
/// </remarks>
public class RetryLaterException(TimeSpan after, string reason)
    : Exception($"{{{{retryAfter:{Math.Max(0, (long)Math.Ceiling(after.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}}}}} {reason}")
{
    public TimeSpan After { get; } = after;

    /// <summary>The wait a Retry-After header asks for, as seconds or as a date; null when there is none.</summary>
    public static TimeSpan? From(System.Net.Http.Headers.RetryConditionHeaderValue? header, DateTimeOffset now) => header switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date > now ? date - now : TimeSpan.Zero,
        _ => null,
    };
}
