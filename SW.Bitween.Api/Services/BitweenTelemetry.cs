using System;
using System.Collections.Generic;
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

    /// <summary>Subscriptions paused for failing too many deliveries in a row.</summary>
    public static readonly Counter<long> SubscriptionsAutoPaused =
        Meter.CreateCounter<long>("bitween.subscriptions.auto_paused",
            description: "Subscriptions paused after failing too many deliveries in a row");

    /// <summary>
    /// Database commands one stage of one exchange sent, tagged <c>stage</c>: gateway (creation and
    /// the sync wait), process, result.
    /// </summary>
    public static readonly Histogram<int> DbCommands =
        Meter.CreateHistogram<int>("bitween.exchanges.db_commands", unit: "{command}",
            description: "Database commands per exchange, by stage");

    /// <summary>
    /// Full parses of a payload into a tree that one stage of one exchange made, tagged <c>stage</c>
    /// like <see cref="DbCommands"/> (processing-performance.md, finding 6).
    /// </summary>
    public static readonly Histogram<int> PayloadParses =
        Meter.CreateHistogram<int>("bitween.exchanges.payload_parses", unit: "{parse}",
            description: "Payload parses per exchange, by stage");

    /// <summary>
    /// Time spent in one stage of an exchange, tagged <c>stage</c>: pickup (created to picked off the
    /// queue), storage.read, storage.write, filter, mapper, handler, save, publish, gateway.create,
    /// gateway.wait.
    /// </summary>
    public static readonly Histogram<double> StageDuration =
        Meter.CreateHistogram<double>("bitween.exchanges.stage.duration", unit: "s",
            description: "Time spent in one stage of an exchange");

    /// <summary>
    /// Times a stage until disposed: a span under the current one and a <see cref="StageDuration"/>
    /// value. <c>using (BitweenTelemetry.Stage("filter")) { ... }</c>
    /// </summary>
    public static StageTimer Stage(string stage) => new(stage);

    /// <summary>Ends once, however many times it is disposed.</summary>
    public sealed class StageTimer : IDisposable
    {
        readonly string stage;
        readonly long started;
        readonly Activity activity;
        int ended;

        internal StageTimer(string stage)
        {
            this.stage = stage;
            started = Stopwatch.GetTimestamp();
            activity = ActivitySource.StartActivity("exchange." + stage);
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref ended, 1) == 1) return;
            StageDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object>("stage", stage));
            activity?.Dispose();
        }
    }
}
