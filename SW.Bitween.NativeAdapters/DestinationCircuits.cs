using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet.Common;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// When a destination counts as unwell, and for how long calls to it then fail at once.
/// </summary>
/// <param name="Failures">Unwell results in a row that open the circuit. 0 switches the breaker off.</param>
/// <param name="WindowSeconds">The run of failures must fall within this many seconds.</param>
/// <param name="OpenSeconds">How long an open circuit fails calls before it lets one through to try.</param>
public sealed record CircuitBreakerSettings(int Failures = 5, int WindowSeconds = 60, int OpenSeconds = 60)
{
    public static CircuitBreakerSettings Default { get; } = new();
    public static CircuitBreakerSettings Off { get; } = new(0);
}

/// <summary>A call refused because its destination's circuit is open. It never reached the destination.</summary>
public sealed class CircuitOpenException(string destination, string message) : Exception(message)
{
    public string Destination { get; } = destination;
}

/// <summary>
/// A circuit breaker per destination, shared by the built-in outbound adapters of this process.
/// </summary>
/// <remarks>
/// <para>
/// A delivery to a partner that has stopped answering holds its place in the processing queue for
/// its whole timeout. Enough of them filled every place, and flows to healthy partners waited
/// behind them. Once a destination has failed <see cref="CircuitBreakerSettings.Failures"/> times in
/// a row, calls to it fail at once, as a 503 so retry policies that retry a 503 retry them, and the
/// queue moves on. After the cool-down one call is let through to try: success closes the circuit,
/// failure opens it again.
/// </para>
/// <para>
/// Only failures that say the destination itself is unwell count: timeouts, refused or dropped
/// connections, 429, and 5xx other than 501. A 4xx is the partner rejecting one message, and is no
/// reason to stop sending it others. Each replica learns on its own; nothing is shared.
/// </para>
/// </remarks>
public sealed class DestinationCircuits
{
    /// <summary>The same meter name as Bitween's own, so the exporter that takes those takes these.</summary>
    static readonly Meter Meter = new("Bitween");

    static readonly Counter<long> Opened = Meter.CreateCounter<long>("bitween.circuits.opened",
        description: "Destination circuits opened after failing too many calls in a row");

    /// <summary>No breaker: every call goes through. What an adapter built without one uses.</summary>
    public static DestinationCircuits None { get; } = new(CircuitBreakerSettings.Off);

    readonly CircuitBreakerSettings settings;
    readonly TimeProvider time;
    readonly ILogger logger;
    readonly ConcurrentDictionary<string, Circuit> circuits = new(StringComparer.OrdinalIgnoreCase);

    public DestinationCircuits(CircuitBreakerSettings settings, TimeProvider? time = null,
        ILogger<DestinationCircuits>? logger = null)
    {
        this.settings = settings;
        this.time = time ?? TimeProvider.System;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public bool Enabled => settings.Failures > 0;

    TimeSpan Window => TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds));
    TimeSpan Cooldown => TimeSpan.FromSeconds(Math.Max(1, settings.OpenSeconds));

    /// <summary>
    /// Admits a call to <paramref name="destination"/>, or throws <see cref="CircuitOpenException"/>
    /// when its circuit is open. The pass is told how the call went; disposed untold, it changes nothing.
    /// </summary>
    public CircuitPass Enter(string destination)
    {
        // Only destinations that have failed have a circuit, so a healthy one costs a lookup.
        if (!Enabled || !circuits.TryGetValue(destination, out var circuit))
            return new CircuitPass(this, destination, null, trial: false);

        lock (circuit)
        {
            var now = time.GetUtcNow();
            switch (circuit.State)
            {
                case CircuitState.Closed:
                    return new CircuitPass(this, destination, circuit, trial: false);

                case CircuitState.Open when now >= circuit.OpenUntil:
                case CircuitState.HalfOpen when now >= circuit.TrialStarted + Cooldown:
                    // A trial that never reported back (its call left hanging) doesn't keep the circuit shut.
                    circuit.State = CircuitState.HalfOpen;
                    circuit.TrialStarted = now;
                    return new CircuitPass(this, destination, circuit, trial: true);

                default:
                    throw new CircuitOpenException(destination,
                        $"503: ServiceUnavailable: circuit open for {destination} ({circuit.Reason}; " +
                        (circuit.State == CircuitState.HalfOpen
                            ? "a trial call is under way)"
                            : $"trying again after {circuit.OpenUntil.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}Z)"));
            }
        }
    }

    internal void Report(string destination, Circuit? circuit, bool trial, Outcome outcome)
    {
        if (!Enabled) return;
        if (circuit is null)
        {
            if (outcome != Outcome.Unwell) return;
            circuit = circuits.GetOrAdd(destination, _ => new Circuit());
        }

        string? opened = null;
        var closed = false;
        lock (circuit)
        {
            var now = time.GetUtcNow();
            switch (outcome)
            {
                case Outcome.Well:
                    // The destination answered: whatever state it was in, it is reachable again.
                    closed = circuit.State != CircuitState.Closed;
                    circuit.State = CircuitState.Closed;
                    circuit.Failures.Clear();
                    break;

                case Outcome.Unwell when circuit.State == CircuitState.Closed:
                    circuit.Failures.Enqueue(now);
                    while (circuit.Failures.Count > 0 && now - circuit.Failures.Peek() > Window)
                        circuit.Failures.Dequeue();
                    if (circuit.Failures.Count >= settings.Failures)
                        opened = Open(circuit, now, $"{circuit.Failures.Count} failures in a row");
                    break;

                case Outcome.Unwell when circuit.State == CircuitState.HalfOpen && trial:
                    opened = Open(circuit, now, "the trial call failed");
                    break;

                case Outcome.Neutral when circuit.State == CircuitState.HalfOpen && trial:
                    // Said nothing about the destination (a bad template, a refused login): let the next call try.
                    circuit.TrialStarted = DateTimeOffset.MinValue;
                    break;

                // A late failure from a call admitted before the circuit opened says nothing new.
            }
        }

        if (opened is not null)
        {
            Opened.Add(1, new KeyValuePair<string, object?>("destination", destination));
            logger.LogWarning(
                "Circuit for {Destination} opened ({Reason}): calls to it fail at once until {Until:u}, then one is let through to try.",
                destination, opened, circuit.OpenUntil);
        }
        else if (closed)
            logger.LogInformation("Circuit for {Destination} closed: it answered again.", destination);
    }

    string Open(Circuit circuit, DateTimeOffset now, string reason)
    {
        circuit.State = CircuitState.Open;
        circuit.OpenUntil = now + Cooldown;
        circuit.Reason = reason;
        circuit.Failures.Clear();
        return reason;
    }

    /// <summary>Whether an HTTP status says the destination is unwell rather than that it rejected this one message.</summary>
    /// <remarks>
    /// 500 counts: one message that breaks the partner gets a 500, but it takes a run of them with no
    /// success between to open the circuit, and that run is a partner in trouble. 501 doesn't: it
    /// means this endpoint will never do what was asked, which a pause won't change.
    /// </remarks>
    public static bool IsUnwell(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests ||
        ((int)status >= 500 && status != HttpStatusCode.NotImplemented);

    /// <summary>Whether a failure says the destination couldn't be reached or didn't answer in time.</summary>
    public static bool IsUnwell(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case CircuitOpenException:
                    return false;
                case TimeoutException or OperationCanceledException or SocketException:
                case IOException and not (FileNotFoundException or DirectoryNotFoundException):
                case SshConnectionException or SshOperationTimeoutException or ProxyException:
                case SmtpProtocolException:
                case SmtpCommandException { StatusCode: SmtpStatusCode.ServiceNotAvailable }:
                    return true;
                case HttpRequestException { StatusCode: { } status }:
                    return IsUnwell(status);
                case HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded }:
                    return true;
            }
        }
        return false;
    }

    internal enum Outcome { Neutral, Well, Unwell }

    internal enum CircuitState { Closed, Open, HalfOpen }

    internal sealed class Circuit
    {
        public CircuitState State = CircuitState.Closed;
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset OpenUntil;
        public DateTimeOffset TrialStarted;
        public string Reason = "";
    }
}

/// <summary>One admitted call. Told <see cref="Succeeded"/> or <see cref="Failed"/>; disposed untold, it is neutral.</summary>
public sealed class CircuitPass : IDisposable
{
    readonly DestinationCircuits circuits;
    readonly string destination;
    readonly DestinationCircuits.Circuit? circuit;
    readonly bool trial;
    bool reported;

    internal CircuitPass(DestinationCircuits circuits, string destination, DestinationCircuits.Circuit? circuit, bool trial)
    {
        this.circuits = circuits;
        this.destination = destination;
        this.circuit = circuit;
        this.trial = trial;
    }

    /// <summary>The destination answered, whatever it said.</summary>
    public void Succeeded() => Report(DestinationCircuits.Outcome.Well);

    /// <summary>The destination is unwell: it timed out, refused, dropped the connection, or answered 429 or a 5xx.</summary>
    public void Failed() => Report(DestinationCircuits.Outcome.Unwell);

    /// <summary>Reports how a call that threw went: unwell when the exception says so, otherwise nothing.</summary>
    public void Failed(Exception exception)
    {
        if (DestinationCircuits.IsUnwell(exception)) Failed();
    }

    public void Dispose() => Report(DestinationCircuits.Outcome.Neutral);

    void Report(DestinationCircuits.Outcome outcome)
    {
        if (reported) return;
        reported = true;
        circuits.Report(destination, circuit, trial, outcome);
    }
}
