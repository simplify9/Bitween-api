using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SW.Bitween;

/// <summary>
/// The metrics and traces Bitween emits about its own work. Logs said what happened to one
/// exchange; nothing said how many were flowing, how many failed or how long they took, so a
/// slowdown or a rising failure rate was found by a partner rather than by a dashboard.
/// </summary>
/// <remarks>
/// Recording costs next to nothing when nobody listens. They are exported only when an
/// OpenTelemetry endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>); see Startup.
/// </remarks>
public static class BitweenTelemetry
{
    public const string Name = "Bitween";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>Exchanges created, tagged with how they arrived.</summary>
    public static readonly Counter<long> ExchangesCreated =
        Meter.CreateCounter<long>("bitween.exchanges.created", description: "Exchanges created");

    /// <summary>Exchanges processed, tagged <c>outcome</c>: success, bad_response or error.</summary>
    public static readonly Counter<long> ExchangesProcessed =
        Meter.CreateCounter<long>("bitween.exchanges.processed", description: "Exchanges processed, by outcome");

    /// <summary>Time from picking an exchange off the queue to its result being saved.</summary>
    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("bitween.exchanges.processing.duration", unit: "s",
            description: "Time to process one exchange");
}
